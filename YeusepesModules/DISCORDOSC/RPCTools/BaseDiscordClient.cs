using System;
using System.IO.Pipes;
using System.Text.Json;
using System.Text;
using System.IO;



namespace DISCORDOSC.RPCTools
{
    public class BaseDiscordClient : IDisposable
    {
        private NamedPipeClientStream _pipeClient;
        private readonly object _writeLock = new object();
        private readonly object _readLock = new object();
        private CancellationTokenSource _cts;
        private string _pipeName;
        private readonly object _pipeLock = new();
        private CancellationTokenSource _listenCts;
        // Connect to the IPC
        public void Connect(string pipeName)
        {
            _pipeClient = new NamedPipeClientStream(
                serverName: ".",
                pipeName: pipeName,
                direction: PipeDirection.InOut,          // full-duplex
                options: PipeOptions.Asynchronous      // enable async under the hood
            );
            _pipeClient.Connect(timeout: 1000);
        }

        // Perform a handshake
        public string Handshake(string clientId)
        {
            var payload = new
            {
                v = 1,
                client_id = clientId
            };
            return SendDataAndWait(0, payload);
        }

        // Reconnect the pipe if broken
        private void Reconnect()
        {
            Dispose();
            Connect(_pipeName);
        }

        // Send data and wait for a response
        public string SendDataAndWait(int op, object payload)
        {
            if (_pipeClient == null || !_pipeClient.IsConnected)
            {
                throw new InvalidOperationException("You must connect your client before sending events!");
            }

            try
            {
                // Serialize payload to JSON
                string payloadJson = JsonSerializer.Serialize(payload);
                byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

                // Create a header with operation code and payload length
                byte[] header = BitConverter.GetBytes(op)
                    .Concat(BitConverter.GetBytes(payloadBytes.Length))
                    .ToArray();

                string responseJson = string.Empty;
                lock (_pipeLock)
                {
                    // Write header and payload to the pipe
                    _pipeClient.Write(header, 0, header.Length);
                    _pipeClient.Write(payloadBytes, 0, payloadBytes.Length);
                    _pipeClient.Flush();
                    Console.WriteLine("Payload sent: " + payloadJson);

                    // Read the response
                    byte[] responseHeader = new byte[8];
                    _pipeClient.Read(responseHeader, 0, 8);
                    int statusCode = BitConverter.ToInt32(responseHeader, 0);
                    int responseLength = BitConverter.ToInt32(responseHeader, 4);

                    byte[] responseBytes = new byte[responseLength];
                    _pipeClient.Read(responseBytes, 0, responseLength);
                    Console.WriteLine("Response received: " + responseJson);
                    return responseJson;
                }
            }
            catch (IOException e)
            {
                Console.WriteLine("Pipe communication error: " + e.Message);
                if (e.Message.Contains("broken"))
                {
                    Console.WriteLine("Attempting to reconnect...");
                    Reconnect();
                    return SendDataAndWait(op, payload); // Retry sending the payload
                }
                throw;
            }
        }

        /// <summary>
        /// Fire-and-forget send.  Never blocks on reads.
        /// </summary>
        public void SendCommand(int op, object payload)
        {
            if (_pipeClient == null || !_pipeClient.IsConnected)
                throw new InvalidOperationException(
                    "Must call Connect(...) before sending commands.");

            // 1) Serialize payload
            string json = JsonSerializer.Serialize(payload);
            byte[] body = Encoding.UTF8.GetBytes(json);

            // 2) Build 8-byte header: [ op (4 bytes) | length (4 bytes) ]
            byte[] header = BitConverter.GetBytes(op)
                              .Concat(BitConverter.GetBytes(body.Length))
                              .ToArray();

            // 3) Atomically write header + body
            lock (_writeLock)
            {
                _pipeClient.Write(header, 0, header.Length);
                _pipeClient.Write(body, 0, body.Length);
                _pipeClient.Flush();
            }
        }

        /// <summary>
        /// Start pumping incoming messages.  onEvent is called for each JSON payload.
        /// </summary>
        public void StartListening(Action<JsonElement> onEvent)
        {
            if (_pipeClient == null || !_pipeClient.IsConnected)
                throw new InvalidOperationException(
                    "Must call Connect(...) before listening.");

            _cts = new CancellationTokenSource();
            Task.Run(() =>
            {
                var header = new byte[8];
                try
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        // 1) Read header
                        lock (_readLock)
                        {
                            int got = _pipeClient.Read(header, 0, 8);
                            if (got == 0) break;  // pipe closed
                        }

                        // Extract length
                        int length = BitConverter.ToInt32(header, 4);
                        var body = new byte[length];

                        // 2) Read body
                        lock (_readLock)
                        {
                            int read = 0;
                            while (read < length)
                                read += _pipeClient.Read(body, read, length - read);
                        }

                        // 3) Deserialize + dispatch
                        string json = Encoding.UTF8.GetString(body);
                        var evt = JsonSerializer.Deserialize<JsonElement>(json);
                        onEvent?.Invoke(evt);
                    }
                }
                catch (IOException) { /* pipe broken, swallow or log */ }
            }, _cts.Token);
        }

        public void StopListening()
        {
            _cts?.Cancel();
        }

        public void Dispose()
        {
            StopListening();
            _pipeClient?.Dispose();
            _pipeClient = null;
        }

    }
}
