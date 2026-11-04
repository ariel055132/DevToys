using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevToys.Blazor.BuiltInTools.Base64File;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static DevToys.Api.GUI;

namespace DevToys.UnitTests.Blazor.BuiltInTools;

public class Base64FileGuiToolTests : IDisposable
{
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Base64FileGuiTool _tool;
    private readonly IUIMultiLineTextInput _text;
    private readonly IUIFileSelector _selector;
    private readonly IUIButton _decode;
    private readonly IUIButton _saveText;
    private readonly IUIInfoBar _status;
    private readonly string _destination = Path.GetTempFileName();

    public Base64FileGuiToolTests()
    {
        _storage.Setup(storage => storage.PickSaveFileAsync(It.IsAny<string[]>()))
            .Returns(() => ValueTask.FromResult<FileStream>(new FileStream(_destination, FileMode.OpenOrCreate, FileAccess.Write)));
        _tool = new Base64FileGuiTool(_storage.Object);
        UIToolView view = _tool.View;
        _text = (IUIMultiLineTextInput)view.GetChildElementById("base64-file-text");
        _selector = (IUIFileSelector)view.GetChildElementById("base64-file-selector");
        _decode = (IUIButton)view.GetChildElementById("base64-file-decode");
        _saveText = (IUIButton)view.GetChildElementById("base64-file-save-text");
        _status = (IUIInfoBar)view.GetChildElementById("base64-file-status");
        _storage.Setup(storage => storage.PickSaveFileWithSuggestedNameAsync(It.IsAny<string>(), It.IsAny<string[]>()))
            .Returns(() => ValueTask.FromResult<FileStream>(new FileStream(_destination, FileMode.OpenOrCreate, FileAccess.Write)));
    }

    [Theory]
    [InlineData("sample.pdf")]
    [InlineData("sample.txt")]
    [InlineData("sample.jpg")]
    [InlineData("sample.png")]
    [InlineData("sample.bin")]
    [InlineData("empty.txt")]
    public async Task SelectingFileAndSavingDecodedContentPreservesBytes(string fileName)
    {
        byte[] expected = CreateFile(fileName);
        using var input = new MemoryFile(fileName, expected);
        bool disposed = false;
        input.Disposed += (_, _) => disposed = true;
        await _selector.OnFilesSelectedAction(new[] { input });
        disposed.Should().BeTrue();
        _text.Text.Should().Be(Convert.ToBase64String(expected));
        _decode.IsEnabled.Should().BeTrue();
        _saveText.IsEnabled.Should().BeTrue();
        await _decode.OnClickAction();
        File.ReadAllBytes(_destination).Should().Equal(expected);
        string extension = fileName == "empty.txt" ? ".bin" : Path.GetExtension(fileName);
        _storage.Verify(storage => storage.PickSaveFileWithSuggestedNameAsync(
            "decoded" + extension, new[] { extension == ".bin" ? "*.*" : extension }), Times.Once);
        _status.Severity.Should().Be(UIInfoBarSeverity.Success);
    }

    [Fact]
    public async Task PasteDataUrlAndSaveTruncatesAnOlderLongerFile()
    {
        File.WriteAllBytes(_destination, new byte[1024]);
        _text.Text("data:application/pdf;base64,JVBERi0xLjc=");
        await _decode.OnClickAction();
        File.ReadAllBytes(_destination).Should().Equal(Encoding.ASCII.GetBytes("%PDF-1.7"));
    }

    [Theory]
    [InlineData("sample.png", "")]
    [InlineData("sample.jpg", "")]
    [InlineData("sample.png", "data:application/octet-stream;base64,")]
    [InlineData("sample.png", "data:image/jpeg;base64,")]
    public async Task PastedImageUsesItsActualFormatAndSavesAnOpenableImage(string fileName, string prefix)
    {
        byte[] expected = CreateFile(fileName);
        _text.Text(prefix + Convert.ToBase64String(expected));
        await _decode.OnClickAction();
        string extension = Path.GetExtension(fileName);
        _storage.Verify(storage => storage.PickSaveFileWithSuggestedNameAsync(
            "decoded" + extension, new[] { extension }), Times.Once);
        byte[] saved = File.ReadAllBytes(_destination);
        saved.Should().Equal(expected);
        using var image = Image.Load(saved);
        image.Width.Should().Be(2);
        image.Height.Should().Be(2);
    }

    [Fact]
    public async Task SaveBase64WritesExactUtf8WithoutBomAndTruncatesOldContent()
    {
        File.WriteAllBytes(_destination, new byte[1024]);
        _text.Text("YWJj");
        await _saveText.OnClickAction();
        File.ReadAllBytes(_destination).Should().Equal(Encoding.ASCII.GetBytes("YWJj"));
        _storage.Verify(storage => storage.PickSaveFileAsync(new[] { "*.txt" }), Times.Once);
    }

    [Fact]
    public async Task InvalidInputDoesNotOpenSaveDialogOrChangeExistingFile()
    {
        File.WriteAllText(_destination, "keep this");
        _text.Text("not valid base64!");
        await _decode.OnClickAction();
        _status.Severity.Should().Be(UIInfoBarSeverity.Error);
        _text.IsReadOnly.Should().BeFalse();
        _storage.Verify(storage => storage.PickSaveFileWithSuggestedNameAsync(It.IsAny<string>(), It.IsAny<string[]>()), Times.Never);
        File.ReadAllText(_destination).Should().Be("keep this");

        _text.Text("Zm9v");
        _status.IsOpened.Should().BeFalse();
        await _decode.OnClickAction();
        File.ReadAllText(_destination).Should().Be("foo");
    }

    [Fact]
    public async Task CancelSaveKeepsInputAndRestoresControls()
    {
        _storage.Setup(storage => storage.PickSaveFileWithSuggestedNameAsync(It.IsAny<string>(), It.IsAny<string[]>()))
            .Returns(ValueTask.FromResult<FileStream>(null));
        _text.Text("Zm9v");
        await _decode.OnClickAction();
        _text.Text.Should().Be("Zm9v");
        _text.IsReadOnly.Should().BeFalse();
        _decode.IsEnabled.Should().BeTrue();
        _status.IsOpened.Should().BeFalse();
    }

    [Fact]
    public async Task FailedReadClearsOldOutputAndReleasesFile()
    {
        _text.Text("Zm9v");
        using var input = new MemoryFile("missing.pdf", Array.Empty<byte>(), failRead: true);
        bool disposed = false;
        input.Disposed += (_, _) => disposed = true;
        await _selector.OnFilesSelectedAction(new[] { input });
        disposed.Should().BeTrue();
        _text.Text.Should().BeEmpty();
        _decode.IsEnabled.Should().BeFalse();
        _status.Severity.Should().Be(UIInfoBarSeverity.Error);
    }

    [Fact]
    public async Task SaveErrorIsVisibleAndAllowsRetry()
    {
        _storage.Setup(storage => storage.PickSaveFileWithSuggestedNameAsync(It.IsAny<string>(), It.IsAny<string[]>()))
            .Throws(new UnauthorizedAccessException());
        _text.Text("Zm9v");
        await _decode.OnClickAction();
        _status.Severity.Should().Be(UIInfoBarSeverity.Error);
        _text.Text.Should().Be("Zm9v");
        _decode.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task BusyOperationCannotBeStartedAgainOrOverwrittenBySmartDetection()
    {
        var pending = new TaskCompletionSource<FileStream>();
        _storage.Setup(storage => storage.PickSaveFileAsync(It.IsAny<string[]>()))
            .Returns(new ValueTask<FileStream>(pending.Task));
        _text.Text("Zm9v");
        Task saving = _saveText.OnClickAction().AsTask();
        _text.IsReadOnly.Should().BeTrue();
        _selector.IsEnabled.Should().BeFalse();
        _decode.IsEnabled.Should().BeFalse();
        _tool.OnDataReceived(PredefinedCommonDataTypeNames.Base64Text, "YmFy");
        await _saveText.OnClickAction();
        _text.Text.Should().Be("Zm9v");
        _storage.Verify(storage => storage.PickSaveFileAsync(It.IsAny<string[]>()), Times.Once);
        pending.SetResult(null);
        await saving;
        _selector.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void HasEnglishAndTraditionalChineseResources()
    {
        var manager = new System.Resources.ResourceManager(
            "DevToys.Blazor.BuiltInTools.Base64File.Base64File", typeof(Base64FileGuiTool).Assembly);
        manager.GetString("ShortDisplayTitle", CultureInfo.GetCultureInfo("en-US")).Should().Be("Base64 File");
        manager.GetString("ShortDisplayTitle", CultureInfo.GetCultureInfo("zh-Hant")).Should().Be("Base64 檔案");
    }

    public void Dispose()
    {
        _tool.Dispose();
        File.Delete(_destination);
    }

    private static byte[] CreateFile(string fileName)
    {
        if (fileName.EndsWith(".jpg", StringComparison.Ordinal) || fileName.EndsWith(".png", StringComparison.Ordinal))
        {
            using var image = new Image<Rgba32>(2, 2);
            using var stream = new MemoryStream();
            if (fileName.EndsWith(".png", StringComparison.Ordinal))
            {
                image.SaveAsPng(stream);
            }
            else
            {
                image.SaveAsJpeg(stream);
            }
            return stream.ToArray();
        }

        return fileName switch
        {
            "sample.pdf" => CreatePdf(),
            "sample.txt" => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("測試文字\r\nsecond line\n")).ToArray(),
            "empty.txt" => Array.Empty<byte>(),
            _ => Enumerable.Range(0, 256).Select(value => (byte)value).ToArray()
        };
    }

    private static byte[] CreatePdf()
    {
        string[] objects =
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> /Contents 4 0 R >>",
            "<< /Length 0 >>\nstream\nendstream"
        };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append("xref\n0 5\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        pdf.Append($"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private sealed class MemoryFile(string name, byte[] content, bool failRead = false) : SandboxedFileReader(name)
    {
        protected override ValueTask<Stream> OpenReadFileAsync(CancellationToken cancellationToken)
        {
            if (failRead)
            {
                throw new IOException();
            }

            return ValueTask.FromResult<Stream>(new MemoryStream(content));
        }
    }
}
