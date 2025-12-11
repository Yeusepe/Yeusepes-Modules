using System;
using System.Text.Json;
using Google.Protobuf;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Utils.Protobuf
{
    /// <summary>
    /// Parser for hm://connect-state/v1/connect/volume messages.
    /// Extracts volume information from SetVolumeCommand protobuf messages.
    /// </summary>
    public class VolumeUpdateParser
    {
        private readonly Action<string> _logDebug;
        private readonly SpotifyRequestContext _requestContext;
        private readonly Action<int> _setVolumePercent;
        private readonly Action<string> _triggerEvent;

        public VolumeUpdateParser(
            SpotifyRequestContext requestContext,
            Action<string> logDebug,
            Action<int> setVolumePercent,
            Action<string> triggerEvent)
        {
            _requestContext = requestContext ?? throw new ArgumentNullException(nameof(requestContext));
            _logDebug = logDebug ?? throw new ArgumentNullException(nameof(logDebug));
            _setVolumePercent = setVolumePercent ?? throw new ArgumentNullException(nameof(setVolumePercent));
            _triggerEvent = triggerEvent ?? throw new ArgumentNullException(nameof(triggerEvent));
        }

        /// <summary>
        /// Handle hm://connect-state/v1/connect/volume messages by decoding the SetVolumeCommand
        /// protobuf payload and mapping it to a 0-100 volume percentage.
        /// </summary>
        public void HandleConnectVolumeUpdate(JsonElement message)
        {
            try
            {
                if (!message.TryGetProperty("payloads", out var payloads))
                {
                    _logDebug("No payloads found in connect-state volume update message");
                    return;
                }

                foreach (var payload in payloads.EnumerateArray())
                {
                    if (payload.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var base64Payload = payload.GetString();
                    if (string.IsNullOrWhiteSpace(base64Payload))
                    {
                        continue;
                    }

                    byte[] decodedBytes;
                    try
                    {
                        decodedBytes = Convert.FromBase64String(base64Payload);
                    }
                    catch (FormatException ex)
                    {
                        _logDebug($"Failed to decode base64 payload in connect-state volume update: {ex.Message}");
                        continue;
                    }

                    _logDebug($"Decoded protobuf bytes length (connect-state volume): {decodedBytes.Length}");

                    try
                    {
                        // Parse spotify.connectstate.SetVolumeCommand directly from protobuf wire format
                        // SetVolumeCommand has field 1 as volume (uint32)
                        using var input = new CodedInputStream(decodedBytes);
                        int? rawVolume = null;
                        
                        while (!input.IsAtEnd)
                        {
                            var tag = input.ReadTag();
                            var fieldNumber = WireFormat.GetTagFieldNumber(tag);
                            var wireType = WireFormat.GetTagWireType(tag);
                            
                            if (fieldNumber == 1 && wireType == WireFormat.WireType.Varint)
                            {
                                // Volume field is uint32
                                rawVolume = (int)input.ReadUInt32();
                                break;
                            }
                            else
                            {
                                input.SkipLastField();
                            }
                        }
                        
                        if (!rawVolume.HasValue)
                        {
                            _logDebug("SetVolumeCommand protobuf did not contain 'volume' field.");
                            continue;
                        }

                        // Map raw volume to 0–100 range.
                        // connect-state volume is typically 0..65535. If the value is already <= 100, use it directly.
                        int volumePercent;
                        if (rawVolume <= 100)
                        {
                            volumePercent = rawVolume.Value;
                        }
                        else
                        {
                            volumePercent = (int)Math.Round(rawVolume.Value * 100.0 / 65535.0);
                        }

                        volumePercent = Math.Clamp(volumePercent, 0, 100);

                        if (_requestContext.VolumePercent != volumePercent)
                        {
                            _requestContext.VolumePercent = volumePercent;
                            _setVolumePercent(volumePercent);
                            _logDebug($"Volume updated from connect-state: raw={rawVolume}, mapped={volumePercent}%");

                            // Fire the VolumeEvent OSC event
                            _triggerEvent("VolumeEvent");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logDebug($"Error parsing SetVolumeCommand protobuf: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logDebug($"Error handling connect-state volume update: {ex.Message}");
            }
        }
    }
}


