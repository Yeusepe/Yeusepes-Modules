using System.Security.Cryptography;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using YeusepesModules.SPOTIOSC.Credentials;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Spotify;

internal sealed class SpotifyConnectCommandClient
{
    private readonly SpotifyRequestContext _context;
    private readonly Action<string> _logDebug;

    public SpotifyConnectCommandClient(SpotifyRequestContext context, Action<string> logDebug)
    {
        _context = context;
        _logDebug = logDebug;
    }

    public Task SetShuffleAsync(int mode) => SendAsync("shuffle", mode, new
    {
        shuffling_context = mode > 0,
        modes = new { context_enhancement = mode == 2 ? "RECOMMENDATION" : "NONE" },
        logging_params = new
        {
            page_instance_ids = new[] { Guid.NewGuid().ToString() },
            interaction_ids = new[] { Guid.NewGuid().ToString() },
            command_id = Guid.NewGuid().ToString("N")
        },
        endpoint = "set_options"
    });

    public Task SetRepeatAsync(int mode) => SendAsync("repeat", mode, new
    {
        repeating_context = mode > 0,
        repeating_track = mode == 1,
        endpoint = "set_options",
        logging_params = new { command_id = Guid.NewGuid().ToString("N") }
    });

    private async Task SendAsync(string option, int mode, object command)
    {
        if (string.IsNullOrEmpty(_context.DeviceId))
        {
            _logDebug($"Cannot set {option} mode: no active device");
            return;
        }

        var accessToken = CredentialManager.LoadAccessToken();
        var clientToken = CredentialManager.LoadClientToken();
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(clientToken))
        {
            _logDebug($"Cannot set {option} mode: missing tokens");
            return;
        }

        var sourceDeviceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
        var url = $"https://gue1-spclient.spotify.com/connect-state/v1/player/command/from/{sourceDeviceId}/to/{_context.DeviceId}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { command }), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("accept", "*/*");
        request.Headers.Add("accept-language", "en-US,en;q=0.9");
        request.Headers.Add("authorization", $"Bearer {accessToken}");
        request.Headers.Add("client-token", clientToken);
        request.Headers.Add("sec-ch-ua", "\"Google Chrome\";v=\"141\", \"Not?A_Brand\";v=\"8\", \"Chromium\";v=\"141\"");
        request.Headers.Add("sec-ch-ua-mobile", "?0");
        request.Headers.Add("sec-ch-ua-platform", "\"Windows\"");
        request.Headers.Add("sec-fetch-dest", "empty");
        request.Headers.Add("sec-fetch-mode", "cors");
        request.Headers.Add("sec-fetch-site", "same-site");

        using var response = await _context.HttpClient.SendAsync(request);
        _logDebug(response.IsSuccessStatusCode
            ? $"Successfully set {option} mode to {mode}"
            : $"Failed to set {option} mode: {response.StatusCode}");
    }
}
