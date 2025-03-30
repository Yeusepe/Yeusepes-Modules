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
        SaveImagesToggle        
    }


    public class StringEncoder
    {       
        private const int MillisecondsDelay = 150;  
        EncodingUtilities encodingUtilities = new EncodingUtilities();

        public bool isEncoding = false;


        public StringEncoder(
            EncodingUtilities encodingUtilities,
            Action<Enum, ModuleSetting> createCustomSetting,
            Action<Enum, string, string, bool> createToggle            
            )
        {
            
            this.encodingUtilities = encodingUtilities;            
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

            sendParameter(EncodingParameter.CharIn, 255);
            isEncoding = false;
        }




    }
}