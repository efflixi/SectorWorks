using System.Text;
using SectorWorks.Core;

namespace SectorWorks.Services;

public sealed class TextPreviewHandler : ISafePreviewHandler
{
    public PreviewResult Preview(Stream input, string name, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (maximumBytes > 65536)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes), "Text preview is limited to 64 KiB.");
        }
        byte[] bytes = new byte[maximumBytes + 1];
        long? remaining = input.CanSeek ? checked(input.Length - input.Position) : null;
        if (remaining < 0)
        {
            throw new InvalidDataException("Preview stream position is beyond its advertised length.");
        }
        int count = 0;
        while (count < bytes.Length)
        {
            int read = input.Read(bytes.AsSpan(count));
            if (read == 0)
            {
                break;
            }
            count += read;
        }
        if (remaining is long length && count < Math.Min(length, bytes.Length))
        {
            throw new EndOfStreamException("File data ended before its advertised preview length.");
        }
        bool truncated = count > maximumBytes;
        count = Math.Min(count, maximumBytes);
        bool utf16 = count >= 2 && ((bytes[0] == 0xff && bytes[1] == 0xfe) || (bytes[0] == 0xfe && bytes[1] == 0xff));
        Encoding encoding = utf16 ? new UnicodeEncoding(bytes[0] == 0xfe, false, true) : new UTF8Encoding(false, true);
        string text;
        try
        {
            char[] characters = new char[count];
            encoding.GetDecoder().Convert(bytes.AsSpan(0, count), characters, flush: !truncated,
                out _, out int charactersUsed, out _);
            text = new string(characters, 0, charactersUsed).TrimStart('\ufeff');
        }
        catch (DecoderFallbackException)
        {
            return new PreviewResult(string.Empty, truncated, "Unsupported text encoding or binary content; use Hex View.");
        }
        if (text.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t'))
        {
            return new PreviewResult(string.Empty, truncated, "Binary content is not rendered as text; use Hex View.");
        }
        return new PreviewResult(text, truncated, $"{name}: plain text ({encoding.WebName}), maximum 64 KiB; content is never executed.");
    }
}
