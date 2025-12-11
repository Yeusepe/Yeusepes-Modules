using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Utils.Events
{
    /// <summary>
    /// Low-level WebSocket client for talking to Spotify's dealer service.
    /// This mirrors librespot's Dealer connection:
    ///   wss://dealer.spotify.com/?access_token={token}
    /// and forwards raw JSON dealer messages to higher-level handlers.
    /// </summary>
    public class DealerWebSocket
    {
        private readonly string _accessToken;
        private readonly string _webSocketUrl;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Fired for every incoming dealer message with type == \"message\".
        /// The JsonElement is the full dealer envelope (headers, payloads, uri, etc.).
        /// </summary>
        public event Action<JsonElement> OnMessageReceived;

        public DealerWebSocket(SpotifyRequestContext spotifyRequestContext)
        {
            _accessToken = spotifyRequestContext.AccessToken;
            _httpClient = spotifyRequestContext.HttpClient;
            _webSocketUrl = $"wss://dealer.spotify.com/?access_token={_accessToken}";
        }

        public async Task StartAsync()
        {
            _webSocket = new ClientWebSocket();
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await _webSocket.ConnectAsync(new Uri(_webSocketUrl), _cancellationTokenSource.Token);

                // Start receiving messages
                _ = Task.Run(() => ReceiveMessagesAsync(_cancellationTokenSource.Token));

                // Send ping messages periodically
                _ = Task.Run(() => KeepAliveAsync(_cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to Dealer WebSocket: {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            if (_webSocket == null)
            {
                return;
            }

            try
            {
                _cancellationTokenSource?.Cancel();

                // Optionally wait a brief moment for tasks to finish
                await Task.Delay(500);

                if (_webSocket.State == WebSocketState.Open ||
                    _webSocket.State == WebSocketState.CloseReceived ||
                    _webSocket.State == WebSocketState.CloseSent)
                {
                    try
                    {
                        await _webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closing",
                            CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Exception while closing Dealer WebSocket: {ex.Message}");
                    }
                }
            }
            finally
            {
                _webSocket.Dispose();
                _webSocket = null;
            }
        }

        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(buffer, cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                        JsonElement jsonMessage;
                        try
                        {
                            jsonMessage = JsonSerializer.Deserialize<JsonElement>(message);
                        }
                        catch (JsonException)
                        {
                            // Ignore malformed messages
                            continue;
                        }

                        ProcessWebSocketMessage(jsonMessage);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // Expected when cancellationToken is cancelled, so silently exit.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Dealer WebSocket receive error: {ex.Message}");
            }
        }

        private async Task KeepAliveAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await SendMessageAsync("{\"type\":\"ping\"}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error sending Dealer ping: {ex.Message}");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
                // Expected on cancellation; exit silently.
            }
        }

        private void ProcessWebSocketMessage(JsonElement message)
        {
            if (message.TryGetProperty("type", out var typeElement) &&
                typeElement.ValueKind == JsonValueKind.String &&
                typeElement.GetString() == "message")
            {
                OnMessageReceived?.Invoke(message);

                // For dealer notifications that use the REST notification endpoint, we still
                // need to enable notifications using the Spotify-Connection-Id header.
                if (message.TryGetProperty("headers", out var headers) &&
                    headers.TryGetProperty("Spotify-Connection-Id", out var connectionIdElement) &&
                    connectionIdElement.ValueKind == JsonValueKind.String)
                {
                    var connectionId = connectionIdElement.GetString();
                    if (!string.IsNullOrEmpty(connectionId))
                    {
                        _ = Task.Run(() => EnablePlayerNotificationsAsync(connectionId));
                    }
                }
            }
        }

        private async Task EnablePlayerNotificationsAsync(string connectionId)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

                var requestUri =
                    $"https://api.spotify.com/v1/me/notifications/player?connection_id={connectionId}";
                var response = await _httpClient.PutAsync(requestUri, null);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Failed to enable player notifications: {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error enabling player notifications: {ex.Message}");
            }
        }

        public async Task SendMessageAsync(string message)
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                var messageBytes = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(
                    messageBytes,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken: CancellationToken.None);
            }
        }
    }
}


