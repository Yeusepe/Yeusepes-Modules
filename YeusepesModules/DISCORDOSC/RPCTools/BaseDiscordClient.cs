﻿using System;
using System.IO.Pipes;
using System.Text.Json;
using System.Text;
using System.IO;



namespace DISCORDOSC.RPCTools
{
    public class BaseDiscordClient : IDisposable
    {
        private NamedPipeClientStream _pipeClient;
        private string _pipeName;
        private readonly object _pipeLock = new();
        private CancellationTokenSource _listenCts;
        // Connect to the IPC
        public void Connect(string pipeName)
        {
            _pipeName = pipeName;
            if (_pipeClient == null || !_pipeClient.IsConnected)
            {
                _pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                _pipeClient.Connect();                
            }
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

        // Dispose method to properly close the pipe
        public void Dispose()
        {
            if (_pipeClient != null)
            {
                _pipeClient.Close();
                _pipeClient.Dispose();
                _pipeClient = null;
                Console.WriteLine("Pipe connection closed.");
            }
        }

        public void StartListening(Action<JsonElement> onEvent)
        {
            if (_pipeClient == null)
                throw new InvalidOperationException("Client not connected");

            _listenCts = new CancellationTokenSource();
            var token = _listenCts.Token;
            Task.Run(() =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        JsonElement evt;
                        lock (_pipeLock)
                        {
                            byte[] header = new byte[8];
                            int read = _pipeClient.Read(header, 0, 8);
                            if (read == 0) break;
                            int op = BitConverter.ToInt32(header, 0);
                            int len = BitConverter.ToInt32(header, 4);
                            byte[] data = new byte[len];
                            _pipeClient.Read(data, 0, len);
                            string json = Encoding.UTF8.GetString(data);
                            evt = JsonSerializer.Deserialize<JsonElement>(json);
                        }
                        onEvent?.Invoke(evt);
                    }
                }
                catch (Exception)
                {
                    // swallow exceptions for simplicity
                }
            }, token);
        }

        public void StopListening()
        {
            _listenCts?.Cancel();
        }
    }
}
