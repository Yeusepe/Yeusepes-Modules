using System.Text;
using YeusepesModules.IDC.Encoder;
using VRCOSC.App.Utils;

public class StringDecoder
{
    private bool isDecoding = false;
    private int expectedChunks = 0;
    private int receivedChunks = 0;
    private StringBuilder[] bitGroups;
    private const int NumParallelBools = 8; // Number of parallel Boolean parameters
    private const int UrlBits = 7; // Number of bits per URL character
    private const int UrlCharCount = 85; // URL character set size

    private StringBuilder chunkTotalBinary = new StringBuilder();
    private StringBuilder chunkIndexBinary = new StringBuilder();

    public event Action<string> DecodingCompleted;

    public StringDecoder()
    {
        ResetDecodingState();
    }

    public void HandleParameter(EncodingParameter parameter, bool value)
    {
        Logger.LogPrint($"Handling parameter: {parameter} with value: {value}");
        Logger.LogPrint($"isDecoding: {isDecoding}, expectedChunks: {expectedChunks}, receivedChunks: {receivedChunks}");
        switch (parameter)
        {
            case EncodingParameter.Decode:
                Logger.LogPrint($"Decode parameter received. Value: {value}");
                if (value)
                {
                    StartDecoding();
                }
                else
                {
                    if (isDecoding && receivedChunks == expectedChunks)
                    {
                        CompleteDecoding();
                    }
                    else
                    {
                        Logger.LogPrint("Decoding aborted due to incomplete data.");
                        ResetDecodingState();
                    }                    
                }
                break;

            case EncodingParameter.ChunkTotal:
                Logger.LogPrint($"ChunkTotal parameter received. Value: {value}");
                if (isDecoding)
                {
                    ProcessBinaryInput(chunkTotalBinary, value, 16, (binary) =>
                    {
                        expectedChunks = Convert.ToInt32(binary, 2);
                        Logger.LogPrint($"Expected Chunks: {expectedChunks}");

                        // Initialize bitGroups only after ChunkTotal is known
                        bitGroups = new StringBuilder[NumParallelBools];
                        for (int i = 0; i < NumParallelBools; i++)
                        {
                            bitGroups[i] = new StringBuilder();
                        }
                    });
                }
                break;

            case EncodingParameter.ChunkIndex:
                Logger.LogPrint($"ChunkIndex parameter received. Value: {value}");
                if (isDecoding && expectedChunks > 0) // Ensure ChunkTotal has been received
                {
                    ProcessBinaryInput(chunkIndexBinary, value, 16, (binary) =>
                    {
                        int chunkIndex = Convert.ToInt32(binary, 2);
                        Logger.LogPrint($"Processing Chunk Index: {chunkIndex}");
                        receivedChunks++;
                    });
                }
                break;

            case EncodingParameter.Bit0:
            case EncodingParameter.Bit1:
            case EncodingParameter.Bit2:
            case EncodingParameter.Bit3:
            case EncodingParameter.Bit4:
            case EncodingParameter.Bit5:
            case EncodingParameter.Bit6:
            case EncodingParameter.Bit7:
                if (isDecoding && expectedChunks > 0) // Ensure ChunkTotal has been received
                {
                    int bitIndex = (int)parameter - (int)EncodingParameter.Bit0;
                    bitGroups[bitIndex].Append(value ? '1' : '0');
                }
                break;

            default:
                throw new InvalidOperationException($"Unexpected parameter: {parameter}");
        }
    }


    private void ProcessBinaryInput(StringBuilder buffer, bool value, int bitCount, Action<string> onComplete)
    {
        buffer.Append(value ? '1' : '0');
        Logger.LogPrint($"Buffer: {buffer}");
        if (buffer.Length == bitCount)
        {
            onComplete(buffer.ToString());
            buffer.Clear();
        }
    }

    private void StartDecoding()
    {        
        if (isDecoding)
        {
            Logger.LogPrint("Decoding already in progress.");
            return;
        }

        Logger.LogPrint("Decoding started. Waiting for ChunkTotal...");
        isDecoding = true;
        receivedChunks = 0;
        expectedChunks = 0; // Set initial value to prevent premature decoding
        chunkTotalBinary.Clear();
        chunkIndexBinary.Clear();

        // Initialize bitGroups to avoid null reference issues
        bitGroups = new StringBuilder[NumParallelBools];
        for (int i = 0; i < NumParallelBools; i++)
        {
            bitGroups[i] = new StringBuilder();
        }
    }




    private void ResetDecodingState()
    {
        Logger.LogPrint("Decoding state reset.");
        isDecoding = false;
        receivedChunks = 0;
        expectedChunks = 0;
        chunkTotalBinary.Clear();
        chunkIndexBinary.Clear();
        bitGroups = null;
    }

    private void CompleteDecoding()
    {
        if (bitGroups == null || bitGroups.All(bg => bg.Length == 0))
        {
            Logger.LogPrint("No bits to decode. Decoding aborted.");
            ResetDecodingState();
            return;
        }
        Logger.LogPrint("Decoding complete. Processing bits...");
        StringBuilder combinedBinary = new StringBuilder();
        Logger.LogPrint($"Bit Groups: {string.Join(", ", bitGroups.Select(bg => bg.ToString()))}");
        // Combine bit groups into a single binary string
        int totalBits = bitGroups.Max(bg => bg?.Length ?? 0); // Handle potential null values
        for (int i = 0; i < totalBits; i++)
        {
            for (int j = 0; j < NumParallelBools; j++)
            {
                if (i < bitGroups[j]?.Length)
                {
                    combinedBinary.Append(bitGroups[j][i]);
                }
            }
        }

        // Decode binary to characters
        Logger.LogPrint($"Combined Binary: {combinedBinary}");
        StringBuilder decodedOutput = new StringBuilder();
        for (int i = 0; i < combinedBinary.Length; i += UrlBits)
        {
            string charBits = combinedBinary.ToString(i, Math.Min(UrlBits, combinedBinary.Length - i));
            int charIndex = Convert.ToInt32(charBits, 2);
            decodedOutput.Append(GetCharFromIndex(charIndex, UrlCharCount));
        }

        Logger.LogPrint($"Decoded Output: {decodedOutput}");

        string decodedString = decodedOutput.ToString();
        DecodingCompleted?.Invoke(decodedString);

        ResetDecodingState();
    }


    private char GetCharFromIndex(int index, int charSetSize)
    {
        Logger.LogPrint($"Getting character for index: {index}, charSetSize: {charSetSize}");
        if (charSetSize == UrlCharCount)
        {
            if (index < 10) return (char)('0' + index);
            if (index < 36) return (char)('A' + index - 10);
            if (index < 62) return (char)('a' + index - 36);
            return index switch
            {
                62 => ':',
                63 => '/',
                64 => '.',
                65 => ',',
                66 => '-',
                67 => '_',
                68 => '?',
                69 => '=',
                70 => '&',
                71 => '@',
                72 => '#',
                73 => '%',
                74 => '+',
                75 => '!',
                76 => '~',
                77 => '*',
                78 => '(',
                79 => ')',
                80 => '\'',
                81 => '"',
                82 => '[',
                83 => ']',
                84 => '{',
                85 => '}',
                86 => ' ',
                _ => throw new ArgumentException($"Unsupported character index: {index}")
            };
        }
        throw new ArgumentException($"Unsupported character index: {index}");
    }
}
