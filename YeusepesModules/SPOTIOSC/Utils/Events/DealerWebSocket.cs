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
        private readonly string _clientToken;
        private readonly string _webSocketUrl;
        private readonly HttpClient _httpClient;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly Action<string> _logDebug;

        /// <summary>
        /// Fired for every incoming dealer message with type == "message".
        /// The JsonElement is the full dealer envelope (headers, payloads, uri, etc.).
        /// </summary>
        public event Action<JsonElement> OnMessageReceived;

        public DealerWebSocket(SpotifyRequestContext spotifyRequestContext, Action<string> logDebug = null)
        {
            _accessToken = spotifyRequestContext.AccessToken;
            _clientToken = spotifyRequestContext.ClientToken;
            _httpClient = spotifyRequestContext.HttpClient;
            _webSocketUrl = $"wss://gue1-dealer.spotify.com/?access_token={_accessToken}";
            _logDebug = logDebug ?? ((msg) => { }); // Default to no-op if not provided
        }

        public async Task StartAsync()
        {
            _webSocket = new ClientWebSocket();
            _cancellationTokenSource = new CancellationTokenSource();
            
            _webSocket.Options.SetRequestHeader("Origin", "https://open.spotify.com");
            _webSocket.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36");

            try
            {
                await _webSocket.ConnectAsync(new Uri(_webSocketUrl), _cancellationTokenSource.Token);
                _logDebug($"[DealerWebSocket] Connected successfully. State: {_webSocket.State}");

                // Start receiving messages
                _ = Task.Run(() => ReceiveMessagesAsync(_cancellationTokenSource.Token));

                // Send ping messages periodically
                _ = Task.Run(() => KeepAliveAsync(_cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                _logDebug($"Error connecting to Dealer WebSocket: {ex.Message}");
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
                        _logDebug($"Exception while closing Dealer WebSocket: {ex.Message}");
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
                _logDebug($"[DealerWebSocket] Receive loop started. WebSocket state: {_webSocket?.State}");
                
                int messageCount = 0;
                DateTime lastStatusLog = DateTime.Now;
                while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
                {
                    // Log status every 30 seconds if no messages received
                    if ((DateTime.Now - lastStatusLog).TotalSeconds > 30)
                    {
                        _logDebug($"[DealerWebSocket] Still waiting for messages (received {messageCount} so far, state: {_webSocket?.State}, time since last: {(DateTime.Now - lastStatusLog).TotalSeconds:F1}s)");
                        lastStatusLog = DateTime.Now;
                    }
                    
                    _logDebug($"[DealerWebSocket] About to call ReceiveAsync (messageCount={messageCount}, state={_webSocket?.State})");
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    messageCount++;
                    lastStatusLog = DateTime.Now;
                    _logDebug($"[DealerWebSocket] ReceiveAsync returned: MessageType={result.MessageType}, Count={result.Count}, EndOfMessage={result.EndOfMessage}, CloseStatus={result.CloseStatus}");
                    
                    if (result.MessageType != WebSocketMessageType.Text && result.MessageType != WebSocketMessageType.Close)
                    {
                        _logDebug($"[DealerWebSocket] Received non-text message: Type={result.MessageType}, Count={result.Count}, EndOfMessage={result.EndOfMessage}");
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logDebug($"[DealerWebSocket] Received close message. CloseStatus: {result.CloseStatus}, CloseStatusDescription: {result.CloseStatusDescription}");
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var messageBuilder = new StringBuilder();
                        int totalBytes = result.Count;
                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                        // Handle fragmented messages (messages larger than buffer)
                        int fragmentCount = 1;
                        while (!result.EndOfMessage)
                        {
                            fragmentCount++;
                            result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                            totalBytes += result.Count;
                            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        }

                        var message = messageBuilder.ToString();
                        _logDebug($"[DealerWebSocket] Received text message: {totalBytes} bytes total ({fragmentCount} fragments), first 200 chars: {message.Substring(0, Math.Min(200, message.Length))}");

                        JsonElement jsonMessage;
                        try
                        {
                            jsonMessage = JsonSerializer.Deserialize<JsonElement>(message);
                        }
                        catch (JsonException ex)
                        {
                            _logDebug($"[DealerWebSocket] Failed to parse JSON message: {ex.Message}. Message preview: {message.Substring(0, Math.Min(500, message.Length))}");
                            continue;
                        }

                        try
                        {
                            ProcessWebSocketMessage(jsonMessage);
                        }
                        catch (Exception ex)
                        {
                            _logDebug($"[DealerWebSocket] Error processing message: {ex.Message} ({ex.GetType().Name})");
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        _logDebug($"[DealerWebSocket] Received binary message ({result.Count} bytes), ignoring");
                    }
                }
                
                _logDebug($"[DealerWebSocket] Receive loop exited. Cancelled: {cancellationToken.IsCancellationRequested}, State: {_webSocket?.State}");
            }
            catch (TaskCanceledException)
            {
                _logDebug("[DealerWebSocket] Receive loop cancelled");
            }
            catch (WebSocketException ex)
            {
                _logDebug($"[DealerWebSocket] WebSocket error in receive loop: {ex.Message} (WebSocketError: {ex.WebSocketErrorCode}, State: {_webSocket?.State}, NativeError: {ex.NativeErrorCode})");
                _logDebug($"[DealerWebSocket] WebSocket exception stack: {ex.StackTrace}");
            }
            catch (Exception ex)
            {
                _logDebug($"[DealerWebSocket] Receive error: {ex.Message} (Type: {ex.GetType().Name}, State: {_webSocket?.State})");
                _logDebug($"[DealerWebSocket] Exception stack: {ex.StackTrace}");
            }
        }

        private async Task KeepAliveAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
                {
                    try
                    {
                        await SendMessageAsync("{\"type\":\"ping\"}");
                        _logDebug("[DealerWebSocket] Sent ping");
                    }
                    catch (Exception ex)
                    {
                        _logDebug($"[DealerWebSocket] Error sending ping: {ex.Message} (State: {_webSocket?.State})");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
                _logDebug("[DealerWebSocket] Keep-alive loop cancelled");
            }
        }

        private void ProcessWebSocketMessage(JsonElement message)
        {
            if (message.TryGetProperty("type", out var typeElement) &&
                typeElement.ValueKind == JsonValueKind.String)
            {
                string messageType = typeElement.GetString();
                
                if (messageType == "message")
                {
                    // Log URI for debugging
                    if (message.TryGetProperty("uri", out var uriElement) && uriElement.ValueKind == JsonValueKind.String)
                    {
                        string uri = uriElement.GetString();
                        _logDebug($"[DealerWebSocket] Forwarding message with URI: {uri}");
                    }
                    else
                    {
                        _logDebug($"[DealerWebSocket] Forwarding message without URI");
                    }
                    
                    // Register device with connection_id to enable player state updates
                    // Only do this for the pusher connection message, not for every message
                    if (message.TryGetProperty("uri", out var uriCheck) && 
                        uriCheck.ValueKind == JsonValueKind.String &&
                        uriCheck.GetString().StartsWith("hm://pusher/v1/connections/") &&
                        message.TryGetProperty("headers", out var headers) &&
                        headers.ValueKind == JsonValueKind.Object &&
                        headers.TryGetProperty("Spotify-Connection-Id", out var connectionIdElement) &&
                        connectionIdElement.ValueKind == JsonValueKind.String)
                    {
                        string connectionId = connectionIdElement.GetString();
                        _logDebug($"[DealerWebSocket] Found connection ID, registering device to enable player state updates");                        
                        _ = Task.Run(() => RegisterDeviceForPlayerStateAsync(connectionId));
                    }
                    
                    OnMessageReceived?.Invoke(message);
                }
                else if (messageType == "pong")
                {
                    // Silently handle pong responses
                }
                else
                {
                    _logDebug($"[DealerWebSocket] Received message with type '{messageType}', not forwarding");
                }
            }
        }

        private async Task RegisterDeviceForPlayerStateAsync(string connectionId)
        {
            // We enables player state updates by registering a device with needs_full_player_state=true            
            try
            {
                // Generate a device ID (we'll use a simple hash of connection ID for consistency)
                var deviceIdBytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(connectionId));
                var deviceId = BitConverter.ToString(deviceIdBytes).Replace("-", "").ToLowerInvariant().Substring(0, 40);
                
                // Step 1: Register device with track-playback API (includes connection_id)
                var deviceRegUrl = $"https://gue1-spclient.spotify.com/track-playback/v1/devices";
                var deviceRegRequest = new HttpRequestMessage(HttpMethod.Post, deviceRegUrl);
                deviceRegRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
                if (!string.IsNullOrEmpty(_clientToken))
                {
                    deviceRegRequest.Headers.Add("client-token", _clientToken);
                }
                
                var deviceRegBody = new
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
                            manifest_formats = new string[] { },
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
                    volume = 65535
                };
                
                var deviceRegJson = JsonSerializer.Serialize(deviceRegBody);
                deviceRegRequest.Content = new StringContent(deviceRegJson, Encoding.UTF8, "application/json");
                
                _logDebug($"[DealerWebSocket] Registering device with track-playback API...");
                var deviceRegResponse = await _httpClient.SendAsync(deviceRegRequest);
                
                if (deviceRegResponse.IsSuccessStatusCode)
                {
                    _logDebug($"[DealerWebSocket] Device registered with track-playback API");
                }
                else
                {
                    var error = await deviceRegResponse.Content.ReadAsStringAsync();
                    _logDebug($"[DealerWebSocket] Device registration failed ({deviceRegResponse.StatusCode}): {error.Substring(0, Math.Min(200, error.Length))}");
                }
                
                // Step 2: Register with connect-state API with needs_full_player_state=true
                var connectStateUrl = $"https://gue1-spclient.spotify.com/connect-state/v1/devices/hobs_{deviceId}";
                var connectStateRequest = new HttpRequestMessage(HttpMethod.Put, connectStateUrl);
                connectStateRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
                if (!string.IsNullOrEmpty(_clientToken))
                {
                    connectStateRequest.Headers.Add("client-token", _clientToken);
                }
                connectStateRequest.Headers.Add("x-spotify-connection-id", connectionId);
                
                var connectStateBody = new
                {
                    member_type = "CONNECT_STATE",
                    device = new
                    {
                        device_info = new
                        {
                            capabilities = new
                            {
                                can_be_player = false,
                                hidden = true,
                                needs_full_player_state = true 
                            }
                        }
                    }
                };
                
                var connectStateJson = JsonSerializer.Serialize(connectStateBody);
                connectStateRequest.Content = new StringContent(connectStateJson, Encoding.UTF8, "application/json");
                
                _logDebug($"[DealerWebSocket] Registering with connect-state API (needs_full_player_state=true)...");
                var connectStateResponse = await _httpClient.SendAsync(connectStateRequest);
                
                if (connectStateResponse.IsSuccessStatusCode)
                {
                    _logDebug($"[DealerWebSocket] Successfully registered device with needs_full_player_state=true! Player state updates should now flow. ✓✓✓");
                }
                else
                {
                    var error = await connectStateResponse.Content.ReadAsStringAsync();
                    _logDebug($"[DealerWebSocket] Connect-state registration failed ({connectStateResponse.StatusCode}): {error.Substring(0, Math.Min(200, error.Length))}");
                }
            }
            catch (Exception ex)
            {
                _logDebug($"[DealerWebSocket] Error registering device for player state: {ex.Message} ({ex.GetType().Name})");
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
