using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRCOSC.App.SDK.Parameters;
using VRCOSC.App.Utils;
/*
// Test input string
string testInput = "Hello, World!";
bool isUrl = true; // Set encoding type

Log($"Input: {testInput}");

// Simulate sending parameters
Log("\n--- Encoding and Sending Parameters ---");
decoder.HandleParameter(EncodingParameter.Decode, true);
encoder.SendAsParallelBooleans(testInput, isUrl, (parameter, value) =>
{
    Log($"Sending {parameter}: {value}");
    // Simulate receiving the same parameter in the decoder
    decoder.HandleParameter(parameter, value);
});
// Wait for decoding to complete
await Task.Delay(1000);
decoder.HandleParameter(EncodingParameter.Decode, false);
Log("\n--- Decoding Completed ---");
*/
namespace YeusepesModules.IDC.Encoder
{
    public enum EncodingParameter
    {
        Encode,
        Decode,
        ChunkIndex,
        ChunkTotal,
        Bit0,
        Bit1,
        Bit2,
        Bit3,
        Bit4,
        Bit5,
        Bit6,
        Bit7
    }

    public class StringEncoder
    {
        private const int Base62CharCount = 62;
        private const int UrlCharCount = 85;
        private const int Base62Bits = 6;
        private const int UrlBits = 7;
        private const int MaxChunkSize = 128; // Maximum characters per chunk
        private const int NumParallelBools = 8; // Number of parallel Boolean parameters

        private readonly Dictionary<EncodingParameter, string> RegisteredParameters = new Dictionary<EncodingParameter, string>();
        private readonly StringBuilder[] receivedBitGroups;

        public StringEncoder()
        {
            // Initialize bit groups for decoding
            receivedBitGroups = new StringBuilder[NumParallelBools];
            for (int i = 0; i < NumParallelBools; i++)
            {
                receivedBitGroups[i] = new StringBuilder();
            }
        }

        public void RegisterParameters(Action<EncodingParameter, string, ParameterMode, string, string> registerAction)
        {
            void Register(EncodingParameter parameter, string name, ParameterMode mode, string title, string description)
            {
                registerAction(parameter, name, mode, title, description);
                if (!RegisteredParameters.ContainsKey(parameter))
                {
                    RegisteredParameters.Add(parameter, name);
                }
            }

            // Register general parameters
            Register(EncodingParameter.Encode, "Encoder/Encode", ParameterMode.ReadWrite, "Start Encoding", "True to start encoding");
            Register(EncodingParameter.Decode, "Encoder/Decode", ParameterMode.ReadWrite, "Start Decoding", "True to start decoding");
            Register(EncodingParameter.ChunkIndex, "Encoder/ChunkIndex", ParameterMode.ReadWrite, "Chunk Index", "Current chunk index being processed");
            Register(EncodingParameter.ChunkTotal, "Encoder/ChunkTotal", ParameterMode.ReadWrite,"Chunk Total","Total number of chunks");

            // Register bit parameters
            for (int i = 0; i < NumParallelBools; i++)
            {
                var parameter = (EncodingParameter)Enum.Parse(typeof(EncodingParameter), $"Bit{i}");
                Register(parameter, $"Encoder/Bit{i}", ParameterMode.ReadWrite, $"Bit {i}", $"Boolean for bit {i}");
            }
        }


        public void SendAsParallelBooleans(string input, bool isUrl, Action<EncodingParameter, bool> sendParameter)
        {
            int bitsPerChar = isUrl ? UrlBits : Base62Bits;
            int charSetSize = isUrl ? UrlCharCount : Base62CharCount;

            StringBuilder binary = new();
            foreach (char c in input)
            {
                int index = GetCharIndex(c, charSetSize);
                binary.Append(Convert.ToString(index, 2).PadLeft(bitsPerChar, '0'));
            }

            int chunkSize = MaxChunkSize * bitsPerChar;
            List<string> chunks = ChunkBinary(binary.ToString(), chunkSize);

            //Logger.LogPrint($"Sending {chunks.Count} chunks for {input.Length} characters");

            // Send ChunkTotal first
            SendIntegerAsBooleans(EncodingParameter.ChunkTotal, chunks.Count, 16, sendParameter);

            // Send each chunk
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                //Logger.LogPrint($"Sending chunk {chunkIndex + 1}/{chunks.Count}");
                SendIntegerAsBooleans(EncodingParameter.ChunkIndex, chunkIndex, 16, sendParameter);

                // Stream binary as parallel booleans
                string chunk = chunks[chunkIndex];
                for (int i = 0; i < chunk.Length; i += NumParallelBools)
                {
                    for (int j = 0; j < NumParallelBools && i + j < chunk.Length; j++)
                    {
                        bool value = chunk[i + j] == '1';
                        sendParameter((EncodingParameter)Enum.Parse(typeof(EncodingParameter), $"Bit{j}"), value);
                    }
                }
            }
        }

        private void SendIntegerAsBooleans(EncodingParameter parameter, int value, int bitCount, Action<EncodingParameter, bool> sendParameter)
        {
            // Convert the integer to a binary string with padding
            string binary = Convert.ToString(value, 2).PadLeft(bitCount, '0');
            //Logger.LogPrint($"Binary representation for {parameter}: {binary}");

            // Send each bit explicitly, keeping within the same parameter
            for (int i = 0; i < binary.Length; i++)
            {
                //Logger.LogPrint($"Sending bit {i + 1}/{binary.Length}");
                bool bitValue = binary[i] == '1';
                sendParameter(parameter, bitValue); // Send as the same parameter, not dynamically calculated
            }
        }




        private int GetCharIndex(char c, int charSetSize)
        {
            if (charSetSize == Base62CharCount)
            {
                if (char.IsDigit(c)) return c - '0';
                if (char.IsUpper(c)) return c - 'A' + 10;
                if (char.IsLower(c)) return c - 'a' + 36;
            }
            else if (charSetSize == UrlCharCount)
            {
                if (char.IsDigit(c)) return c - '0';
                if (char.IsUpper(c)) return c - 'A' + 10;
                if (char.IsLower(c)) return c - 'a' + 36;
                return c switch
                {
                    ':' => 62,
                    '/' => 63,
                    '.' => 64,
                    ',' => 65,
                    '-' => 66,
                    '_' => 67,
                    '?' => 68,
                    '=' => 69,
                    '&' => 70,
                    '@' => 71,
                    '#' => 72,
                    '%' => 73,
                    '+' => 74,
                    '!' => 75,
                    '~' => 76,
                    '*' => 77,
                    '(' => 78,
                    ')' => 79,
                    '\'' => 80,
                    '"' => 81,
                    '[' => 82,
                    ']' => 83,
                    '{' => 84,
                    '}' => 85,
                    ' ' => 86, // Handle space as a special character
                    _ => throw new ArgumentException($"Unsupported character for URL encoding: {c}")
                };
            }
            throw new ArgumentException($"Unsupported character: {c}");
        }



        private List<string> ChunkBinary(string binary, int chunkSize)
        {
            List<string> chunks = new List<string>();
            for (int i = 0; i < binary.Length; i += chunkSize)
            {
                chunks.Add(binary.Substring(i, Math.Min(chunkSize, binary.Length - i)));
            }
            return chunks;
        }        
    }
}
