using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.SDK.Parameters;
using VRCOSC.App.Utils;
using YeusepesModules.Common.ScreenUtilities;

namespace YeusepesModules.IDC.Encoder
{
    public enum EncodingParameter
    {
        CharIn,
        Touching,
        Ready
    }

    public enum EncoderSettings
    {
        SaveImagesToggle,
        Tolerance
    }


    public class StringEncoder
    {       
        private const int MillisecondsDelay = 150;
        private const int MillisecondsDelay2 = 5000; // For the second delay
        EncodingUtilities encodingUtilities = new EncodingUtilities();

        public bool isEncoding = false;

        private CancellationTokenSource _cts;

        public StringEncoder(
            EncodingUtilities encodingUtilities,
            Action<Enum, string, string, string> CreateTextBox,
            Action<Enum, string> setSettingValue,
            Func<Enum, string> getSettingValue // New delegate for reading settings
        )
        {
            // Register the tolerance setting with a default value of 100.
            CreateTextBox(EncoderSettings.Tolerance, "Tolerance", "Tolerance value for image filtering", 100.ToString());

            // Save the provided dependencies.
            this.encodingUtilities = encodingUtilities;
            encodingUtilities.SetSettingValue = setSettingValue;
            encodingUtilities.GetSettingValue = getSettingValue;  // Ensure the getter is wired!
        }


        public void RegisterParameters(Action<Enum, string, ParameterMode, string, string> registerIntParameter, Action<Enum, string, ParameterMode, string, string> registerBoolParameter)
        {
            registerIntParameter(EncodingParameter.CharIn, "Encoder/CharIn", ParameterMode.Write, "Number Representing ASCII Character", "ASCII character to encode");
            registerBoolParameter(EncodingParameter.Touching, "Encoder/Touching", ParameterMode.Read, "Touching", "Indicates if the encoder is currently encoding");
            registerBoolParameter(EncodingParameter.Ready, "Encoder/Ready", ParameterMode.Read, "Ready", "Indicates if the decoder is ready to receive the data");
        }

        public void CancelEncoding()
        {
            _cts?.Cancel();
        }

        public async void SendString(string input, bool isUrl, Action<EncodingParameter, int> sendParam)
        {
            if (isEncoding) return;
            if (string.IsNullOrEmpty(input)) return;

            // kill any pending encode
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            isEncoding = true;
            try
            {
                // start signal
                sendParam(EncodingParameter.CharIn, input.Length);
                await Task.Delay(MillisecondsDelay, token);
                sendParam(EncodingParameter.CharIn, 0);
                await Task.Delay(MillisecondsDelay, token);

                foreach (char c in input)
                {
                    token.ThrowIfCancellationRequested();

                    int intValue = ((int)c) % 256;
                    encodingUtilities.LogDebug($"Sending character {c} as {intValue}");
                    sendParam(EncodingParameter.CharIn, intValue);
                    await Task.Delay(MillisecondsDelay, token);

                    sendParam(EncodingParameter.CharIn, 0);
                    await Task.Delay(MillisecondsDelay, token);
                }

                // end-of-string marker
                await Task.Delay(MillisecondsDelay2, token);
                sendParam(EncodingParameter.CharIn, 255);
            }
            catch (OperationCanceledException)
            {
                sendParam(EncodingParameter.CharIn, 254);
                encodingUtilities.LogDebug("Encoding cancelled.");
            }
            finally
            {
                isEncoding = false;
            }
        }
    }
}