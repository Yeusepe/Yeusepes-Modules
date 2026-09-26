using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace YeusepesModules.SPOTIOSC.Runtime.Jam;

internal sealed record JamInvitation(byte[] Secret, string Lookup, string Owner, string Ciphertext);

internal sealed class JamInvitationClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri _base;
    public JamInvitationClient(string serverUrl, HttpClient? testClient = null)
    {
        _base = new Uri(serverUrl.TrimEnd('/') + "/syncopation/v2/invitations/");
        if (_base.Scheme != "https" && !(_base.Scheme == "http" && _base.IsLoopback)) throw new ArgumentException("Invitation relay requires HTTPS");
        _ownsHttp = testClient is null;
        // Separate transport: Spotify headers, cookies and redirect destinations never receive capabilities.
        _http = testClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(10) };
    }
    public static JamInvitation Create(string session)
    {
        var secret = JamBeaconProtocol.NewSecret();
        return new(secret, JamBeaconProtocol.Lookup(secret), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(), Seal(secret, session));
    }
    private static string Seal(byte[] secret, string session) => JamBeaconProtocol.Encrypt(secret,
        JsonSerializer.Serialize(new { session, expires = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 90 }));
    public static JamInvitation Renew(JamInvitation invitation, string session) => invitation with { Ciphertext = Seal(invitation.Secret, session) };
    public async Task PublishAsync(JamInvitation invitation, CancellationToken cancellation)
    {
        using var response = await _http.PostAsJsonAsync(new Uri(_base, "publish"), new { lookup = invitation.Lookup, owner = invitation.Owner, ciphertext = invitation.Ciphertext }, cancellation);
        response.EnsureSuccessStatusCode();
    }
    public async Task<string> ResolveAsync(byte[] secret, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_base, "resolve")) { Content = JsonContent.Create(new { lookup = JamBeaconProtocol.Lookup(secret) }) };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        // A hostile relay must not cause an unbounded response allocation.
        await response.Content.LoadIntoBufferAsync(2048);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        using var payload = JsonDocument.Parse(JamBeaconProtocol.Decrypt(secret, json.RootElement.GetProperty("ciphertext").GetString()!));
        var expiry = payload.RootElement.GetProperty("expires").GetInt64();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (expiry <= now || expiry > now + 120) throw new CryptographicException("Invitation expired");
        return payload.RootElement.GetProperty("session").GetString() ?? throw new CryptographicException("Invalid invitation");
    }
    public async Task RevokeAsync(JamInvitation invitation)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var response = await _http.PostAsJsonAsync(new Uri(_base, "revoke"), new { lookup = invitation.Lookup, owner = invitation.Owner }, cancellation.Token);
        response.EnsureSuccessStatusCode();
    }
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
