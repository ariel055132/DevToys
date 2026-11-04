using System.Text;

namespace DevToys.Blazor.BuiltInTools.Base64File;

[Export(typeof(IGuiTool))]
[Name("Base64FileEncoderDecoder")]
[ToolDisplayInformation(
    IconFontName = "DevToys-Tools-Icons",
    IconGlyph = '\u0100',
    GroupName = PredefinedCommonToolGroupNames.EncodersDecoders,
    ResourceManagerAssemblyIdentifier = nameof(DevToysBlazorResourceManagerAssemblyIdentifier),
    ResourceManagerBaseName = "DevToys.Blazor.BuiltInTools.Base64File.Base64File",
    ShortDisplayTitleResourceName = nameof(Base64FileStrings.ShortDisplayTitle),
    LongDisplayTitleResourceName = nameof(Base64FileStrings.LongDisplayTitle),
    DescriptionResourceName = nameof(Base64FileStrings.Description),
    AccessibleNameResourceName = nameof(Base64FileStrings.LongDisplayTitle),
    SearchKeywordsResourceName = nameof(Base64FileStrings.SearchKeywords))]
[AcceptedDataTypeName(PredefinedCommonDataTypeNames.Base64Text)]
internal sealed class Base64FileGuiTool : IGuiTool, IDisposable
{
    private readonly IFileStorage _fileStorage;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly IUIFileSelector _fileSelector = FileSelector("base64-file-selector");
    private readonly IUIMultiLineTextInput _text = MultiLineTextInput("base64-file-text");
    private readonly IUIButton _decode = Button("base64-file-decode");
    private readonly IUIButton _saveText = Button("base64-file-save-text");
    private readonly IUIInfoBar _status = InfoBar("base64-file-status");
    private readonly UIToolView _view;
    private bool _busy;
    private bool _disposed;
    private bool _hasEncodedFile;

    [ImportingConstructor]
    public Base64FileGuiTool(IFileStorage fileStorage)
    {
        _fileStorage = fileStorage;
        _view = new UIToolView(
            isScrollable: true,
            Stack().Vertical().LargeSpacing().WithChildren(
                Label().Text(Base64FileStrings.Instructions).WrapIfNeeded(),
                _fileSelector.CanSelectOneFile().OnFilesSelected(OnFilesSelectedAsync),
                _text.Title(Base64FileStrings.Base64Text).AlwaysWrap().CanCopyWhenEditable()
                    .OnTextChanged(OnTextChanged),
                Wrap().WithChildren(
                    _decode.Text(Base64FileStrings.DecodeAndSave).AccentAppearance().OnClick(DecodeAndSaveAsync),
                    _saveText.Text(Base64FileStrings.SaveBase64).OnClick(SaveTextAsync)),
                _status));
        UpdateControls();
    }

    public UIToolView View => _view;

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
        if (!_busy && !_disposed && parsedData is string text)
        {
            _text.Text(text);
        }
    }

    private void OnTextChanged(string text)
    {
        _hasEncodedFile = false;
        if (!_busy)
        {
            _status.Close();
        }
        UpdateControls();
    }

    private async ValueTask OnFilesSelectedAsync(SandboxedFileReader[] files)
    {
        if (files.Length == 0)
        {
            return;
        }

        try
        {
            await RunAsync(async () =>
            {
                // A failed new selection must not leave the previous file's Base64 ready to save.
                _text.Text(string.Empty);
                _hasEncodedFile = false;
                using Stream source = await files[0].GetNewAccessToFileContentAsync(_cancellation.Token);
                string encoded = await Task.Run(() => Base64FileConverter.EncodeAsync(source, _cancellation.Token));
                _cancellation.Token.ThrowIfCancellationRequested();
                _text.Text(encoded);
                _hasEncodedFile = true;
                _status.Success().Title(string.Format(Base64FileStrings.Encoded, files[0].FileName)).Open();
            });
        }
        finally
        {
            foreach (SandboxedFileReader file in files)
            {
                file.Dispose();
            }
        }
    }

    private async ValueTask DecodeAndSaveAsync()
    {
        string input = _text.Text;
        await RunAsync(async () =>
        {
            // Validate before opening the save dialog so invalid input cannot truncate an existing file.
            (byte[] decoded, string extension) = await Task.Run(() =>
            {
                byte[] bytes = Base64FileConverter.Decode(input, _cancellation.Token);
                return (bytes, Base64FileFormat.DetectExtension(bytes));
            });
            _cancellation.Token.ThrowIfCancellationRequested();
            string suggestedName = "decoded" + extension;
            _status.Informational().Title(extension == ".bin"
                ? Base64FileStrings.UnknownFormat
                : string.Format(Base64FileStrings.DetectedFormat, extension.TrimStart('.').ToUpperInvariant())).Open();
            using FileStream? destination = await _fileStorage.PickSaveFileWithSuggestedNameAsync(
                suggestedName, extension == ".bin" ? "*.*" : extension);
            if (destination is null)
            {
                _status.Close();
                return;
            }

            destination.SetLength(0);
            await destination.WriteAsync(decoded, _cancellation.Token);
            await destination.FlushAsync(_cancellation.Token);
            _status.Success().Title(string.Format(Base64FileStrings.Saved, Path.GetFileName(destination.Name), decoded.Length)).Open();
        });
    }

    private async ValueTask SaveTextAsync()
    {
        string input = _text.Text;
        await RunAsync(async () =>
        {
            using FileStream? destination = await _fileStorage.PickSaveFileAsync("*.txt");
            if (destination is null)
            {
                _status.Close();
                return;
            }

            destination.SetLength(0);
            using var writer = new StreamWriter(destination, new UTF8Encoding(false));
            await writer.WriteAsync(input.AsMemory(), _cancellation.Token);
            await writer.FlushAsync(_cancellation.Token);
            _status.Success().Title(Base64FileStrings.TextSaved).Open();
        });
    }

    private async ValueTask RunAsync(Func<Task> operation)
    {
        if (_busy || _disposed)
        {
            return;
        }

        _busy = true;
        UpdateControls();
        _status.Informational().Title(Base64FileStrings.Working).Open();
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (FormatException)
        {
            _status.Error().Title(Base64FileStrings.InvalidBase64).Open();
        }
        catch (InvalidDataException)
        {
            _status.Error().Title(Base64FileStrings.SizeLimit).Open();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _status.Error().Title(Base64FileStrings.FileError).Open();
        }
        finally
        {
            _busy = false;
            if (_disposed)
            {
                _cancellation.Dispose();
            }
            else
            {
                UpdateControls();
            }
        }
    }

    private void UpdateControls()
    {
        bool hasInput = _hasEncodedFile || !string.IsNullOrWhiteSpace(_text.Text);
        if (_busy)
        {
            _fileSelector.Disable();
            _text.ReadOnly();
        }
        else
        {
            _fileSelector.Enable();
            _text.Editable();
        }

        if (_busy || !hasInput)
        {
            _decode.Disable();
            _saveText.Disable();
        }
        else
        {
            _decode.Enable();
            _saveText.Enable();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _cancellation.Cancel();
            if (!_busy)
            {
                _cancellation.Dispose();
            }
        }
    }
}
