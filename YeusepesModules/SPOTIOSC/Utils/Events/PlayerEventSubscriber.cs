using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using YeusepesModules.SPOTIOSC.Utils.Requests;
using System.Net.Http;

namespace YeusepesModules.SPOTIOSC.Utils.Events
{
    [Obsolete("PlayerEventSubscriber has been superseded by DealerWebSocket and is no longer used.")]
    public class PlayerEventSubscriber
    {
        private readonly string _accessToken;
        private readonly string _webSocketUrl;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource;
        protected HttpClient httpClient { get; }

        public event Action<JsonElement> OnPlayerEventReceived;

        public PlayerEventSubscriber(SpotifyUtilities spotifyUtilities, SpotifyRequestContext spotifyRequestContext)
        {
            _accessToken = spotifyRequestContext.AccessToken;
            httpClient = spotifyRequestContext.HttpClient;
            _webSocketUrl = $"wss://gue1-dealer.spotify.com/?access_token={_accessToken}";
            // Log response

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
                Console.WriteLine($"Error connecting to WebSocket: {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            if (_webSocket != null)
            {
                // Cancel background tasks
                _cancellationTokenSource.Cancel();

                // Optionally wait a brief moment for tasks to finish
                await Task.Delay(500);

                // Check if the WebSocket is in a state that allows graceful closure
                if (_webSocket.State == WebSocketState.Open ||
                    _webSocket.State == WebSocketState.CloseReceived ||
                    _webSocket.State == WebSocketState.CloseSent)
                {
                    try
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);                        
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Exception while closing WebSocket: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"WebSocket is in an invalid state for graceful closure: {_webSocket.State}");
                }
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

                        var jsonMessage = JsonSerializer.Deserialize<JsonElement>(message);
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
                        Console.WriteLine($"Error sending ping: {ex.Message}");
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
            if (message.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "message")
            {
                OnPlayerEventReceived?.Invoke(message);

                if (message.TryGetProperty("headers", out var headers) &&
                    headers.TryGetProperty("Spotify-Connection-Id", out var connectionId))
                {                    
                    _ = Task.Run(() => EnablePlayerNotificationsAsync(connectionId.GetString()));
                }
            }
        }

        private async Task EnablePlayerNotificationsAsync(string connectionId)
        {
            try
            {
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

                var requestUri = $"https://api.spotify.com/v1/me/notifications/player?connection_id={connectionId}";
                var response = await httpClient.PutAsync(requestUri, null);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Enabled player notifications for this connection.");
                }
                else
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
                await _webSocket.SendAsync(messageBytes, WebSocketMessageType.Text, true, CancellationToken.None);                
            }
        }
    }
}
