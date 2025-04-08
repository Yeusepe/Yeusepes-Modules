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


        public StringEncoder(
            EncodingUtilities encodingUtilities,
            Action<Enum, string, string, string> CreateTextBox,
            Action<Enum, string> setSettingValue,
            Func<Enum, string> getSettingValue // New delegate for reading settings
        )
        {
            // Register the tolerance setting with a default value of 100.
            CreateTextBox(EncoderSettings.Tolerance, "Tolerance", "Tolerance value for image filtering", 3.ToString());

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


        public async void SendString(string input, bool isUrl, Action<EncodingParameter, int> sendParameter)
        {
            if (string.IsNullOrEmpty(input))
                return;

            isEncoding = true;

            // Send start signal: number of characters in the string, then a 0
            sendParameter(EncodingParameter.CharIn, input.Length);
            await Task.Delay(MillisecondsDelay);

            sendParameter(EncodingParameter.CharIn, 0);
            await Task.Delay(MillisecondsDelay);

            // For each character, send its integer value (ensured to be 0-255) and then a 0
            foreach (char c in input)
            {
                int intValue = ((int)c) % 256;

                encodingUtilities.LogDebug($"Sending character {c} as {intValue}");
                sendParameter(EncodingParameter.CharIn, intValue);
                await Task.Delay(MillisecondsDelay);

                sendParameter(EncodingParameter.CharIn, 0);
                await Task.Delay(MillisecondsDelay);
            }

            await Task.Delay(MillisecondsDelay2);
            sendParameter(EncodingParameter.CharIn, 255);
            isEncoding = false;
        }




    }
}