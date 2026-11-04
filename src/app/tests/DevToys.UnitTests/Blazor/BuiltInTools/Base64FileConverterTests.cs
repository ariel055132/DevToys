using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevToys.Blazor.BuiltInTools.Base64File;

namespace DevToys.UnitTests.Blazor.BuiltInTools;

public class Base64FileConverterTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "Zg==")]
    [InlineData("fo", "Zm8=")]
    [InlineData("foo", "Zm9v")]
    [InlineData("foobar", "Zm9vYmFy")]
    public async Task StandardVectors(string plainText, string base64)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(plainText);
        using var source = new MemoryStream(bytes);
        (await Base64FileConverter.EncodeAsync(source, CancellationToken.None)).Should().Be(base64);
        Base64FileConverter.Decode(base64, CancellationToken.None).Should().Equal(bytes);
    }

    [Theory]
    [InlineData(" Z m\t9\rv\n")]
    [InlineData("data:text/plain;charset=utf-8;base64,Zm9v")]
    [InlineData("DATA:application/pdf;BASE64,Zm9v")]
    public void AcceptsWrappedTextAndDataUrls(string input)
    {
        Base64FileConverter.Decode(input, CancellationToken.None).Should().Equal(Encoding.UTF8.GetBytes("foo"));
    }

    [Theory]
    [InlineData("Zg")]
    [InlineData("data:;base64,Zg%3D%3D")]
    public void AcceptsMissingPaddingAndUrlEscapes(string input)
    {
        Base64FileConverter.Decode(input, CancellationToken.None).Should().Equal((byte)'f');
    }

    [Theory]
    [InlineData("+/8=")]
    [InlineData("-_8=")]
    [InlineData("-_8")]
    public void DecodesBinaryWithoutTextTranscoding(string input)
    {
        Base64FileConverter.Decode(input, CancellationToken.None).Should().Equal(251, 255);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("Zg=")]
    [InlineData("Z===")]
    [InlineData("Zm=v")]
    [InlineData("Zm9v!")]
    [InlineData("Zm9v\u200b")]
    [InlineData("+/8_")]
    [InlineData("data:text/plain,Zm9v")]
    [InlineData("data:text/plain;base64=1,Zm9v")]
    [InlineData("data:text/plain;base64")]
    [InlineData("data:;base64,%GG")]
    public void RejectsInvalidInput(string input)
    {
        Action decode = () => Base64FileConverter.Decode(input, CancellationToken.None);
        decode.Should().Throw<FormatException>();
    }

    [Fact]
    public async Task PreservesEveryByteIncludingTextBomAndLineEndings()
    {
        byte[] bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("繁體中文\r\nSecond line\n"))
            .Concat(Enumerable.Range(0, 256).Select(value => (byte)value)).ToArray();
        using var stream = new MemoryStream(bytes);
        string encoded = await Base64FileConverter.EncodeAsync(stream, CancellationToken.None);
        Base64FileConverter.Decode(encoded, CancellationToken.None).Should().Equal(bytes);
    }

    [Fact]
    public async Task EnforcesDecodedSizeAtTheBoundary()
    {
        byte[] bytes = new byte[Base64FileConverter.MaximumFileSize];
        bytes[^1] = 255;
        using var stream = new MemoryStream(bytes);
        string encoded = await Base64FileConverter.EncodeAsync(stream, CancellationToken.None);
        Base64FileConverter.Decode(encoded, CancellationToken.None).Should().Equal(bytes);

        string tooLarge = Convert.ToBase64String(new byte[bytes.Length + 1]);
        Action decode = () => Base64FileConverter.Decode(tooLarge, CancellationToken.None);
        decode.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsOversizeFilesIncludingNonSeekableStreams(bool canSeek)
    {
        using var source = new LimitedStream(new byte[Base64FileConverter.MaximumFileSize + 1], canSeek);
        Func<Task> encode = () => Base64FileConverter.EncodeAsync(source, CancellationToken.None);
        await encode.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public void RejectsExcessiveWhitespaceBeforeAllocatingConversionBuffers()
    {
        string input = new(' ', Base64FileConverter.MaximumInputLength + 1);
        Action decode = () => Base64FileConverter.Decode(input, CancellationToken.None);
        decode.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task CancellationStopsConversion()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var stream = new MemoryStream(new byte[100]);
        Func<Task> encode = () => Base64FileConverter.EncodeAsync(stream, cancellation.Token);
        await encode.Should().ThrowAsync<OperationCanceledException>();
        Action decode = () => Base64FileConverter.Decode("Zm9v", cancellation.Token);
        decode.Should().Throw<OperationCanceledException>();
    }

    private sealed class LimitedStream(byte[] data, bool canSeek) : MemoryStream(data)
    {
        public override bool CanSeek => canSeek;
    }
}
