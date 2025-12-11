using System;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Utils.Protobuf
{
    /// <summary>
    /// Parser for playback-settings/content-settings-update protobuf messages.
    /// Extracts smart shuffle information from ContextPlayerState and ContextPlayerOptions.
    /// </summary>
    public class ContentSettingsParser
    {
        private readonly Action<string> _logDebug;
        private readonly SpotifyRequestContext _requestContext;
        private readonly Action<int> _setShuffleMode;
        private readonly Action<bool> _setShuffleState;

        public ContentSettingsParser(
            SpotifyRequestContext requestContext,
            Action<string> logDebug,
            Action<int> setShuffleMode,
            Action<bool> setShuffleState)
        {
            _requestContext = requestContext ?? throw new ArgumentNullException(nameof(requestContext));
            _logDebug = logDebug ?? throw new ArgumentNullException(nameof(logDebug));
            _setShuffleMode = setShuffleMode ?? throw new ArgumentNullException(nameof(setShuffleMode));
            _setShuffleState = setShuffleState ?? throw new ArgumentNullException(nameof(setShuffleState));
        }

        /// <summary>
        /// Handle playback-settings/content-settings-update messages by decoding protobuf payloads
        /// and extracting smart shuffle information.
        /// </summary>
        public void HandleContentSettingsUpdate(JsonElement message)
        {
            try
            {
                _logDebug("HandleContentSettingsUpdate called");
                if (!message.TryGetProperty("payloads", out var payloads))
                {
                    _logDebug("No payloads found in content-settings-update message");
                    return;
                }

                foreach (var payload in payloads.EnumerateArray())
                {
                    _logDebug($"Processing payload, ValueKind: {payload.ValueKind}");

                    // Payload is a base64-encoded string containing protobuf data
                    if (payload.ValueKind != JsonValueKind.String)
                    {
                        _logDebug($"Payload is not a string, it's: {payload.ValueKind}");
                        continue;
                    }

                    var base64Payload = payload.GetString();
                    if (string.IsNullOrWhiteSpace(base64Payload))
                    {
                        continue;
                    }

                    try
                    {
                        byte[] decodedBytes = Convert.FromBase64String(base64Payload);
                        _logDebug($"Decoded protobuf bytes length (content-settings-update): {decodedBytes.Length}");

                        if (!TryUpdateSmartShuffleFromContentSettings(decodedBytes))
                        {
                            _logDebug("Unable to extract smart shuffle from content-settings-update protobuf payload.");
                        }
                    }
                    catch (FormatException ex)
                    {
                        _logDebug($"Failed to decode base64 payload in content-settings-update: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        _logDebug($"Error parsing protobuf in content-settings-update: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logDebug($"Error handling content settings update: {ex.Message}");
            }
        }

        private bool TryUpdateSmartShuffleFromContentSettings(byte[] decodedBytes)
        {
            try
            {
                _logDebug($"TryUpdateSmartShuffleFromContentSettings: Starting parse, bytes length={decodedBytes.Length}");

                // The payload we see on the wire has the following structure (inferred from bytes):
                // 
                // message ContentSettingsUpdate {
                //   // field 1 is currently unknown / unused in our samples
                //   string context_uri = 2;   // e.g. "spotify:playlist:..."
                //   repeated ModeSetting modes = 3;
                // }
                //
                // message ModeSetting {
                //   uint32 mode_id = 1;        // 4 = shuffle, 5 = smart_shuffle (inferred)
                //   ModeValue value = 2;       // nested message, contains a boolean
                // }
                //
                // message ModeValue {
                //   // oneof over different numeric fields; we only care about "is non‑zero"
                //   // In practice we've seen:
                //   //   field 2 (tag 0x10) used with value 1
                //   //   field 3 (tag 0x18) used with value 0/1
                // }
                //
                // Example decoded bytes for one payload:
                //   12 27 "spotify:playlist:...."
                //   1A 06 08 04 12 02 10 01   -> mode_id=4, bool=true
                //   1A 06 08 05 12 02 18 00   -> mode_id=5, bool=false
                //
                // We therefore parse this message generically and look for mode_id 5 as smart shuffle.

                using var input = new CodedInputStream(decodedBytes);

                string contextUri = null;
                bool? shuffleEnabled = null;
                bool? smartShuffleEnabled = null;
                int fieldCount = 0;

                while (!input.IsAtEnd)
                {
                    var tag = input.ReadTag();
                    var fieldNumber = WireFormat.GetTagFieldNumber(tag);
                    var wireType = WireFormat.GetTagWireType(tag);
                    fieldCount++;

                    _logDebug($"TryUpdateSmartShuffleFromContentSettings: Field #{fieldCount}, number={fieldNumber}, wireType={wireType}");

                    switch (fieldNumber)
                    {
                        case 2 when wireType == WireFormat.WireType.LengthDelimited:
                            contextUri = input.ReadString();
                            _logDebug($"TryUpdateSmartShuffleFromContentSettings: context_uri='{contextUri}'");
                            break;

                        case 3 when wireType == WireFormat.WireType.LengthDelimited:
                            var settingBytes = input.ReadBytes().ToByteArray();
                            if (TryParseModeSetting(settingBytes, out int modeId, out bool? enabled))
                            {
                                _logDebug($"TryUpdateSmartShuffleFromContentSettings: Parsed ModeSetting modeId={modeId}, enabled={enabled}");

                                if (modeId == 4)
                                {
                                    shuffleEnabled = enabled;
                                }
                                else if (modeId == 5)
                                {
                                    smartShuffleEnabled = enabled;
                                }
                            }
                            else
                            {
                                _logDebug("TryUpdateSmartShuffleFromContentSettings: Failed to parse ModeSetting");
                            }
                            break;

                        default:
                            _logDebug($"TryUpdateSmartShuffleFromContentSettings: Skipping field {fieldNumber} (wireType={wireType})");
                            input.SkipLastField();
                            break;
                    }
                }

                _logDebug($"TryUpdateSmartShuffleFromContentSettings: Finished parse, shuffleEnabled={shuffleEnabled}, smartShuffleEnabled={smartShuffleEnabled}");

                // Apply both shuffle and smart shuffle if we have them
                bool hasUpdates = false;
                
                if (shuffleEnabled.HasValue)
                {
                    _logDebug($"TryUpdateSmartShuffleFromContentSettings: Updating shuffle state to {shuffleEnabled.Value}");
                    _requestContext.ShuffleState = shuffleEnabled.Value;
                    _setShuffleState(shuffleEnabled.Value);
                    hasUpdates = true;
                }
                
                if (smartShuffleEnabled.HasValue)
                {
                    _logDebug($"TryUpdateSmartShuffleFromContentSettings: Updating smart shuffle to {smartShuffleEnabled.Value}");
                    _requestContext.SmartShuffle = smartShuffleEnabled.Value;
                    hasUpdates = true;
                }
                
                // Calculate and set ShuffleMode based on both values
                if (hasUpdates)
                {
                    bool currentShuffle = shuffleEnabled ?? _requestContext.ShuffleState;
                    bool currentSmart = smartShuffleEnabled ?? _requestContext.SmartShuffle;
                    
                    int shuffleMode = !currentShuffle ? 0 : (currentSmart ? 2 : 1);
                    _logDebug($"TryUpdateSmartShuffleFromContentSettings: Setting ShuffleMode={shuffleMode} (shuffle={currentShuffle}, smart={currentSmart})");
                    _setShuffleMode(shuffleMode);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logDebug($"TryUpdateSmartShuffleFromContentSettings: Exception: {ex.Message}\n{ex.StackTrace}");
            }

            _logDebug($"TryUpdateSmartShuffleFromContentSettings: Returning false - unable to extract smart shuffle");
            return false;
        }
        
        /// <summary>
        /// Parse a single ModeSetting message from the content-settings-update payload.
        /// </summary>
        /// <param name="modeSettingBytes">Wire bytes of the ModeSetting message (field 3).</param>
        /// <param name="modeId">Parsed mode id (e.g. 4=shuffle, 5=smart shuffle).</param>
        /// <param name="enabled">Parsed boolean value, or null if not present.</param>
        /// <returns>true if parsing succeeded, false otherwise.</returns>
        private bool TryParseModeSetting(byte[] modeSettingBytes, out int modeId, out bool? enabled)
        {
            modeId = 0;
            enabled = null;

            try
            {
                using var input = new CodedInputStream(modeSettingBytes);

                while (!input.IsAtEnd)
                {
                    var tag = input.ReadTag();
                    var fieldNumber = WireFormat.GetTagFieldNumber(tag);
                    var wireType = WireFormat.GetTagWireType(tag);

                    if (fieldNumber == 1 && wireType == WireFormat.WireType.Varint)
                    {
                        modeId = input.ReadInt32();
                    }
                    else if (fieldNumber == 2 && wireType == WireFormat.WireType.LengthDelimited)
                    {
                        // Nested ModeValue message; we only care whether any of its numeric fields are non-zero.
                        var valueBytes = input.ReadBytes().ToByteArray();
                        using var valueInput = new CodedInputStream(valueBytes);

                        while (!valueInput.IsAtEnd)
                        {
                            var valueTag = valueInput.ReadTag();
                            var valueFieldNumber = WireFormat.GetTagFieldNumber(valueTag);
                            var valueWireType = WireFormat.GetTagWireType(valueTag);

                            if (valueWireType == WireFormat.WireType.Varint &&
                                (valueFieldNumber == 2 || valueFieldNumber == 3))
                            {
                                var raw = valueInput.ReadInt32();
                                enabled = raw != 0;
                            }
                            else
                            {
                                valueInput.SkipLastField();
                            }
                        }
                    }
                    else
                    {
                        input.SkipLastField();
                    }
                }

                return modeId != 0 && enabled.HasValue;
            }
            catch (Exception ex)
            {
                _logDebug($"TryParseModeSetting: Exception while parsing mode setting: {ex.Message}");
                return false;
            }
        }

    }
}

