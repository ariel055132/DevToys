using System.IO;
using System.Text;
using DevToys.Blazor.BuiltInTools.Base64File;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace DevToys.UnitTests.Blazor.BuiltInTools;

public class Base64FileFormatTests
{
    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".gif")]
    [InlineData(".bmp")]
    [InlineData(".webp")]
    [InlineData(".tiff")]
    public void DetectsImageBytesWithoutAFileNameOrMimeType(string extension)
    {
        IImageEncoder encoder = extension switch
        {
            ".png" => new PngEncoder(),
            ".jpg" => new JpegEncoder(),
            ".gif" => new GifEncoder(),
            ".bmp" => new BmpEncoder(),
            ".webp" => new WebpEncoder(),
            _ => new TiffEncoder()
        };
        using var image = new Image<Rgba32>(2, 2);
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        Base64FileFormat.DetectExtension(stream.ToArray()).Should().Be(extension);
    }

    [Fact]
    public void RecognizesPdfAndPreservesUnknownBinary()
    {
        Base64FileFormat.DetectExtension("%PDF-1.7\n"u8).Should().Be(".pdf");
        Base64FileFormat.DetectExtension(new byte[] { 0, 0x81, 0xff }).Should().Be(".bin");
        Base64FileFormat.DetectExtension(Array.Empty<byte>()).Should().Be(".bin");
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    public void RecognizesUnicodeText(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes("你好，DevToys!\r\n")).ToArray();
        Base64FileFormat.DetectExtension(bytes).Should().Be(".txt");
    }
}
