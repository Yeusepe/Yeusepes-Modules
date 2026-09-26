using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Spotify;

internal sealed class SpotifyDealerClient : IAsyncDisposable
{
    private const string DealerUrl = "wss://gue1-dealer.spotify.com/?access_token=";
    private readonly SpotifyRequestContext _context;
    private readonly Action<string> _logDebug;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _lifetime;
    private Task? _receiveTask;
    private Task? _keepAliveTask;
    private Task? _registrationTask;

    public SpotifyDealerClient(SpotifyRequestContext context, Action<string> logDebug)
    {
        _context = context;
        _logDebug = logDebug;
    }

    public event Action<JsonElement>? MessageReceived;

    public async Task StartAsync()
    {
        if (_socket?.State == WebSocketState.Open) return;
        var lifetime = new CancellationTokenSource();
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", "https://open.spotify.com");
        socket.Options.SetRequestHeader(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36");
        _lifetime = lifetime;
        _socket = socket;

        try
        {
            var token = Uri.EscapeDataString(_context.AccessToken);
            await socket.ConnectAsync(new Uri(DealerUrl + token), lifetime.Token);
            _receiveTask = ReceiveLoopAsync(socket, lifetime.Token);
            _keepAliveTask = KeepAliveLoopAsync(socket, lifetime.Token);
            _logDebug("Dealer WebSocket connected");
        }
        catch (Exception exception)
        {
            _logDebug($"Dealer WebSocket connection failed: {exception.Message}");
            await StopAsync();
        }
    }

    public async Task StopAsync()
    {
        var socket = _socket;
        var lifetime = _lifetime;
        if (socket is null) return;
        _socket = null;
        _lifetime = null;
        lifetime?.Cancel();

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closing",
                    closeTimeout.Token);
            }
            catch (Exception exception) when (
                exception is WebSocketException or OperationCanceledException)
            {
            }
        }

        try
        {
            await Task.WhenAll(new[] { _receiveTask, _keepAliveTask, _registrationTask }
                .Where(task => task is not null)
                .Cast<Task>());
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _receiveTask = null;
            _keepAliveTask = null;
            _registrationTask = null;
            lifetime?.Dispose();
            socket.Dispose();
            _logDebug("Dealer WebSocket stopped");
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8 * 1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult frame;
                do
                {
                    frame = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (frame.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, frame.Count);
                } while (!frame.EndOfMessage);

                if (frame.MessageType != WebSocketMessageType.Text) continue;
                try
                {
                    using var document = JsonDocument.Parse(
                        message.GetBuffer().AsMemory(0, checked((int)message.Length)));
                    ProcessMessage(document.RootElement, cancellationToken);
                }
                catch (JsonException exception)
                {
                    _logDebug($"Dealer sent invalid JSON: {exception.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException exception)
        {
            _logDebug($"Dealer receive failed: {exception.Message}");
        }
        catch (Exception exception)
        {
            _logDebug($"Dealer receive loop failed: {exception.Message}");
        }
    }

    private async Task KeepAliveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(
                    Encoding.UTF8.GetBytes("{\"type\":\"ping\"}"),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logDebug($"Dealer keep-alive failed: {exception.Message}");
        }
    }

    private void ProcessMessage(JsonElement message, CancellationToken cancellationToken)
    {
        if (!message.TryGetProperty("type", out var type) || type.GetString() != "message") return;
        if (TryGetConnectionId(message, out var connectionId) &&
            (_registrationTask is null || _registrationTask.IsCompleted))
            _registrationTask = RegisterDeviceAsync(connectionId, cancellationToken);
        MessageReceived?.Invoke(message);
    }

    private static bool TryGetConnectionId(JsonElement message, out string connectionId)
    {
        connectionId = string.Empty;
        if (!message.TryGetProperty("uri", out var uri) ||
            uri.GetString()?.StartsWith("hm://pusher/v1/connections/", StringComparison.Ordinal) != true ||
            !message.TryGetProperty("headers", out var headers) ||
            !headers.TryGetProperty("Spotify-Connection-Id", out var id))
            return false;
        connectionId = id.GetString() ?? string.Empty;
        return connectionId.Length > 0;
    }

    private async Task RegisterDeviceAsync(string connectionId, CancellationToken cancellationToken)
    {
        try
        {
            var deviceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connectionId)))
                .ToLowerInvariant()[..40];
            using var playbackRequest = CreatePlaybackRegistration(connectionId, deviceId);
            using var playbackResponse = await _context.HttpClient.SendAsync(playbackRequest, cancellationToken);
            if (!playbackResponse.IsSuccessStatusCode)
                _logDebug($"Dealer playback registration failed: {playbackResponse.StatusCode}");

            using var stateRequest = CreateStateRegistration(connectionId, deviceId);
            using var stateResponse = await _context.HttpClient.SendAsync(stateRequest, cancellationToken);
            _logDebug(stateResponse.IsSuccessStatusCode
                ? "Dealer player-state subscription registered"
                : $"Dealer player-state registration failed: {stateResponse.StatusCode}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logDebug($"Dealer device registration failed: {exception.Message}");
        }
    }

    private HttpRequestMessage CreatePlaybackRegistration(string connectionId, string deviceId) =>
        CreateJsonRequest(
            HttpMethod.Post,
            "https://gue1-spclient.spotify.com/track-playback/v1/devices",
            new
            {
                device = new
                {
                    brand = "spotify",
                    capabilities = new
                    {
                        change_volume = true,
                        enable_play_token = false,
                        supports_file_media_type = false,
                        play_token_lost_behavior = "pause",
                        disable_connect = false,
                        audio_podcasts = false,
                        video_playback = false,
                        manifest_formats = Array.Empty<string>(),
                        supports_preferred_media_type = false,
                        supports_playback_offsets = false,
                        supports_playback_speed = false
                    },
                    device_id = deviceId,
                    device_type = "computer",
                    metadata = new { },
                    model = "web_player",
                    name = "VRCOSC",
                    platform_identifier = "web_player windows undefined;chrome 143.0.0.0;desktop",
                    is_group = false
                },
                outro_endcontent_snooping = false,
                connection_id = connectionId,
                client_version = "harmony:4.62.1-5dc29b8a7",
                volume = ushort.MaxValue
            });

    private HttpRequestMessage CreateStateRegistration(string connectionId, string deviceId)
    {
        var request = CreateJsonRequest(
            HttpMethod.Put,
            $"https://gue1-spclient.spotify.com/connect-state/v1/devices/hobs_{deviceId}",
            new
            {
                member_type = "CONNECT_STATE",
                device = new
                {
                    device_info = new
                    {
                        capabilities = new { can_be_player = false, hidden = true, needs_full_player_state = true }
                    }
                }
            });
        request.Headers.Add("x-spotify-connection-id", connectionId);
        return request;
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string url, object body)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _context.AccessToken);
        if (!string.IsNullOrEmpty(_context.ClientToken)) request.Headers.Add("client-token", _context.ClientToken);
        return request;
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
