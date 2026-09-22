using System.Net.Http;
using YeusepesModules.SPOTIOSC.Credentials;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Spotify;

internal sealed class SpotifySessionBootstrapper
{
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(10);
    private readonly Action<string> _log;
    private readonly Action<string> _logDebug;

    public SpotifySessionBootstrapper(Action<string> log, Action<string> logDebug)
    {
        _log = log;
        _logDebug = logDebug;
    }

    public async Task<SpotifyRequestContext?> CreateContextAsync(HttpClient httpClient)
    {
        try
        {
            var tokens = await GetValidatedTokensAsync(httpClient);
            return tokens is null
                ? null
                : new SpotifyRequestContext
                {
                    HttpClient = httpClient,
                    AccessToken = tokens.Value.Access,
                    ClientToken = tokens.Value.Client
                };
        }
        catch (OperationCanceledException)
        {
            _logDebug("Spotify session initialization was cancelled");
        }
        catch (Exception exception)
        {
            _logDebug($"Spotify session initialization failed: {exception.Message}");
        }
        return null;
    }

    private async Task<(string Access, string Client)?> GetValidatedTokensAsync(HttpClient httpClient)
    {
        var tokens = LoadTokens();
        if (!tokens.HasValue)
        {
            if (!await RefreshAsync()) return null;
            tokens = LoadTokens();
        }
        if (!tokens.HasValue) return null;

        if (await ValidateAsync(httpClient, tokens.Value)) return tokens;
        CredentialManager.ClearAllTokensAndCookies();
        if (!await RefreshAsync()) return null;
        tokens = LoadTokens();
        return tokens.HasValue && await ValidateAsync(httpClient, tokens.Value) ? tokens : null;
    }

    private async Task<bool> ValidateAsync(HttpClient httpClient, (string Access, string Client) tokens)
    {
        return await new SpotifySessionValidator(httpClient, _log, _logDebug)
            .ValidateAsync(tokens.Access, tokens.Client);
    }

    private async Task<bool> RefreshAsync()
    {
        try
        {
            await CredentialManager.LoginAndCaptureCookiesAsync().WaitAsync(LoginTimeout);
            return LoadTokens().HasValue;
        }
        catch (TimeoutException)
        {
            _logDebug("Token refresh timed out after 10 minutes");
        }
        catch (Exception exception)
        {
            _logDebug($"Token refresh failed: {exception.Message}");
        }
        return false;
    }

    private static (string Access, string Client)? LoadTokens()
    {
        var access = CredentialManager.LoadAccessToken();
        var client = CredentialManager.LoadClientToken();
        return string.IsNullOrEmpty(access) || string.IsNullOrEmpty(client) ? null : (access, client);
    }
}
