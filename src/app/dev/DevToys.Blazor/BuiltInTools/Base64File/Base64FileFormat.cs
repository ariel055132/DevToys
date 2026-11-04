using System.Text;
using SixLabors.ImageSharp;

namespace DevToys.Blazor.BuiltInTools.Base64File;

internal static class Base64FileFormat
{
    internal static string DetectExtension(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith("%PDF-"u8))
        {
            return ".pdf";
        }

        if (!data.IsEmpty)
        {
            try
            {
                // Inspect the signature only; preserve the original image bytes and metadata.
                string? extension = Image.DetectFormat(data).Name.ToUpperInvariant() switch
                {
                    "PNG" => ".png",
                    "JPEG" => ".jpg",
                    "GIF" => ".gif",
                    "BMP" => ".bmp",
                    "TIFF" => ".tiff",
                    "WEBP" => ".webp",
                    _ => null
                };
                if (extension is not null)
                {
                    return extension;
                }
            }
            catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
            {
                // Arbitrary files are supported even when they are not images.
            }
        }

        if (IsText(data))
        {
            return ".txt";
        }

        return ".bin";
    }

    private static bool IsText(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return false;
        }

        if (data.StartsWith(Encoding.Unicode.Preamble)
            || data.StartsWith(Encoding.BigEndianUnicode.Preamble)
            || data.StartsWith(Encoding.UTF32.Preamble)
            || data.StartsWith(new byte[] { 0, 0, 0xfe, 0xff }))
        {
            return true;
        }

        try
        {
            string text = new UTF8Encoding(false, true).GetString(data);
            return text.All(character => !char.IsControl(character) || character is '\r' or '\n' or '\t');
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
