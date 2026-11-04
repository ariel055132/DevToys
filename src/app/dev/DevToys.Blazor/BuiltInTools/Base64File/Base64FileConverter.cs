using System.Text;

namespace DevToys.Blazor.BuiltInTools.Base64File;

internal static class Base64FileConverter
{
    // Bound both file reads and pasted input before allocating the conversion buffers.
    internal const int MaximumFileSize = 20 * 1024 * 1024;
    internal const int MaximumEncodedLength = ((MaximumFileSize + 2) / 3) * 4;
    internal const int MaximumInputLength = MaximumEncodedLength * 2;

    internal static async Task<string> EncodeAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source.CanSeek && source.Length - source.Position > MaximumFileSize)
        {
            throw new InvalidDataException(Base64FileStrings.SizeLimit);
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > MaximumFileSize)
            {
                throw new InvalidDataException(Base64FileStrings.SizeLimit);
            }

            buffer.Write(chunk, 0, count);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    internal static byte[] Decode(string input, CancellationToken cancellationToken)
    {
        if (input.Length > MaximumInputLength)
        {
            throw new InvalidDataException(Base64FileStrings.SizeLimit);
        }

        string payload = input.Trim();
        if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int comma = payload.IndexOf(',');
            if (comma < 0 || !payload.AsSpan(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException();
            }

            payload = Uri.UnescapeDataString(payload[(comma + 1)..]);
        }

        var normalized = new StringBuilder(Math.Min(payload.Length, MaximumEncodedLength));
        bool standardAlphabet = false;
        bool urlAlphabet = false;
        for (int i = 0; i < payload.Length; i++)
        {
            if ((i & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            char character = payload[i];
            if (character is ' ' or '\t' or '\r' or '\n')
            {
                continue;
            }

            standardAlphabet |= character is '+' or '/';
            urlAlphabet |= character is '-' or '_';
            normalized.Append(character switch { '-' => '+', '_' => '/', _ => character });
            if (normalized.Length > MaximumEncodedLength)
            {
                throw new InvalidDataException(Base64FileStrings.SizeLimit);
            }
        }

        if (standardAlphabet && urlAlphabet)
        {
            throw new FormatException();
        }

        // Accept omitted padding, but never repair misplaced or incomplete explicit padding.
        string base64 = normalized.ToString();
        if (!base64.Contains('='))
        {
            base64 = (base64.Length % 4) switch
            {
                2 => base64 + "==",
                3 => base64 + "=",
                _ => base64
            };
        }

        int padding = base64.EndsWith("==", StringComparison.Ordinal) ? 2 : base64.EndsWith('=') ? 1 : 0;
        if ((long)base64.Length / 4 * 3 - padding > MaximumFileSize)
        {
            throw new InvalidDataException(Base64FileStrings.SizeLimit);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Convert.FromBase64String(base64);
    }
}
