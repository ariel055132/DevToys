using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Text;
using System.Threading.Tasks;
using DevToys.Blazor.BuiltInTools.Settings;
using DevToys.Blazor.Core.Languages;
using DevToys.Core.Mef;
using DevToys.Core.Settings;
using DevToys.Core.Tools;

namespace DevToys.UnitTests.Blazor.BuiltInTools;

public class SettingsPreferencesGuiTests : MefBasedTest, IDisposable
{
    private readonly Mock<IFileStorage> _files = new();
    private readonly ISettingsProvider _settings;
    private readonly PreferencesTransferService _transfer;
    private readonly SettingsGuiTool _tool;
    private readonly UIToolView _view;
    private readonly IUIButton _export;
    private readonly IUIButton _import;
    private readonly IUIInfoBar _status;
    private readonly string _path = Path.GetTempFileName();

    public SettingsPreferencesGuiTests()
    {
        _settings = MefProvider.Import<ISettingsProvider>();
        _transfer = new PreferencesTransferService(_settings, MefProvider.Import<GuiToolProvider>(), MefProvider.Import<SmartDetectionService>());
        var fonts = new Mock<IFontProvider>();
        fonts.Setup(provider => provider.GetFontFamilies()).Returns(new[] { "Menlo", "Monaco" });
        _tool = new SettingsGuiTool(_settings, fonts.Object, Mock.Of<IClipboard>(), _files.Object, _transfer);
        _view = _tool.View;
        _export = (IUIButton)_view.GetChildElementById("export-preferences");
        _import = (IUIButton)_view.GetChildElementById("import-preferences");
        _status = (IUIInfoBar)_view.GetChildElementById("preferences-transfer-status");
        _files.Setup(files => files.PickOpenFileAsync(It.IsAny<string[]>()))
            .Returns(() => ValueTask.FromResult(SandboxedFileReader.FromFileInfo(new FileInfo(_path))));
    }

    [Fact]
    public void SettingsToolLoadsThroughApplicationCompositionWithTransferService()
    {
        using var composer = new MefComposer(new[] { typeof(SettingsGuiTool).Assembly, typeof(SettingsPreferencesGuiTests).Assembly });
        var fonts = new Mock<IFontProvider>();
        fonts.Setup(provider => provider.GetFontFamilies()).Returns(new[] { "Menlo" });
        composer.ExportProvider.ComposeExportedValue(fonts.Object);
        composer.ExportProvider.ComposeExportedValue(Mock.Of<IClipboard>());
        GuiToolProvider tools = composer.Provider.Import<GuiToolProvider>();
        UIToolView view = tools.GetToolFromInternalName("Settings").View;
        view.GetChildElementById("export-preferences").Should().NotBeNull();
        view.GetChildElementById("import-preferences").Should().NotBeNull();
    }

    [Fact]
    public async Task ExportUsesSuggestedJsonFileNameAndWritesCompleteUtf8Backup()
    {
        File.WriteAllText(_path, new string('x', 10000));
        _files.Setup(files => files.PickSaveFileWithSuggestedNameAsync(It.IsAny<string>(), It.IsAny<string[]>()))
            .Returns(() => ValueTask.FromResult(new FileStream(_path, FileMode.OpenOrCreate, FileAccess.Write)));
        string expected = _transfer.Export();

        await _export.OnClickAction();

        File.ReadAllBytes(_path).Should().Equal(new UTF8Encoding(false).GetBytes(expected));
        _files.Verify(files => files.PickSaveFileWithSuggestedNameAsync("DevToys-preferences.json", new[] { "*.json" }), Times.Once);
        _status.Severity.Should().Be(UIInfoBarSeverity.Success);
        _export.IsEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImportWaitsForExplicitConfirmationAndCancelKeepsSettings(bool confirm)
    {
        File.WriteAllText(_path, PreferencesTransferServiceTests.File("\"Theme\":\"Dark\",\"Language\":\"zh-Hant\",\"TextEditorFont\":\"Other Font\""));
        var opened = new TaskCompletionSource<UIDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
        _view.CurrentOpenedDialogChanged += (_, _) =>
        {
            if (_view.CurrentOpenedDialog is UIDialog dialog)
            {
                opened.TrySetResult(dialog);
            }
        };

        Task pendingImport = _import.OnClickAction().AsTask();
        UIDialog confirmation = await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _settings.GetSetting(PredefinedSettings.Theme).Should().Be(AvailableApplicationTheme.Default);
        _import.IsEnabled.Should().BeFalse();
        _export.IsEnabled.Should().BeFalse();
        // Even a second invocation must not open another picker or apply anything.
        await _import.OnClickAction();
        _files.Verify(files => files.PickOpenFileAsync(new[] { "*.json" }), Times.Once);

        var footer = (IUIElementWithChildren)confirmation.FooterContent;
        var action = (IUIButton)footer.GetChildElementById(confirm ? "confirm-preferences-import" : "cancel-preferences-import");
        await action.OnClickAction();
        await pendingImport.WaitAsync(TimeSpan.FromSeconds(10));

        _settings.GetSetting(PredefinedSettings.Theme).Should().Be(confirm ? AvailableApplicationTheme.Dark : AvailableApplicationTheme.Default);
        _import.IsEnabled.Should().BeTrue();
        _export.IsEnabled.Should().BeTrue();
        _view.CurrentOpenedDialog.Should().BeNull();
        _tool.View.Should().BeSameAs(_view);
        if (confirm)
        {
            _status.Severity.Should().Be(UIInfoBarSeverity.Success);
            _status.Description.Should().Contain("Restart");
            var languages = (IUISelectDropDownList)_view.GetChildElementById("language-select-drop-down-list");
            ((LanguageDefinition)languages.SelectedItem.Value).InternalName.Should().Be("zh-Hant");
            var fontSetting = (IUISetting)_view.GetChildElementById("text-editor-font-setting");
            ((IUISelectDropDownList)fontSetting.InteractiveElement).SelectedItem.Value.Should().Be("Other Font");
        }
        else
        {
            _status.IsOpened.Should().BeFalse();
        }
    }

    [Fact]
    public async Task DismissingConfirmationDoesNotImport()
    {
        File.WriteAllText(_path, PreferencesTransferServiceTests.File("\"Theme\":\"Dark\""));
        var opened = new TaskCompletionSource<UIDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
        _view.CurrentOpenedDialogChanged += (_, _) =>
        {
            if (_view.CurrentOpenedDialog is UIDialog dialog)
            {
                opened.TrySetResult(dialog);
            }
        };
        Task pendingImport = _import.OnClickAction().AsTask();
        (await opened.Task.WaitAsync(TimeSpan.FromSeconds(10))).Close();
        await pendingImport.WaitAsync(TimeSpan.FromSeconds(10));
        _settings.GetSetting(PredefinedSettings.Theme).Should().Be(AvailableApplicationTheme.Default);
        _status.IsOpened.Should().BeFalse();
    }

    [Theory]
    [InlineData("not json", "valid DevToys")]
    [InlineData("{\"format\":\"DevToys.Preferences\",\"version\":1,\"settings\":{\"Theme\":\"Dark\"},\"tools\":{\"MissingTool\":{\"isFavorite\":true}}}", "missing tools")]
    public async Task InvalidImportShowsErrorWithoutOpeningConfirmationOrWriting(string json, string expectedError)
    {
        File.WriteAllText(_path, json);
        await _import.OnClickAction();
        _status.Severity.Should().Be(UIInfoBarSeverity.Error);
        _status.Title.Should().Contain(expectedError);
        _view.CurrentOpenedDialog.Should().BeNull();
        _settings.GetSetting(PredefinedSettings.Theme).Should().Be(AvailableApplicationTheme.Default);
        _import.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task CancellingEitherPickerIsSilentAndFileErrorAllowsRetry()
    {
        _files.Setup(files => files.PickOpenFileAsync(It.IsAny<string[]>())).Returns(ValueTask.FromResult<SandboxedFileReader>(null));
        await _import.OnClickAction();
        await _export.OnClickAction();
        _status.IsOpened.Should().BeFalse();
        _import.IsEnabled.Should().BeTrue();
        _export.IsEnabled.Should().BeTrue();

        _files.Setup(files => files.PickOpenFileAsync(It.IsAny<string[]>())).Throws(new IOException("read failure"));
        await _import.OnClickAction();
        _status.Severity.Should().Be(UIInfoBarSeverity.Error);
        _import.IsEnabled.Should().BeTrue();
        _export.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void TransferLabelsHaveEnglishAndTraditionalChineseTranslations()
    {
        var resources = new ResourceManager("DevToys.Blazor.BuiltInTools.Settings.Settings", typeof(SettingsGuiTool).Assembly);
        resources.GetString("ExportPreferences", CultureInfo.GetCultureInfo("en-US")).Should().Be("Export preferences");
        resources.GetString("ImportPreferences", CultureInfo.GetCultureInfo("zh-Hant")).Should().Be("匯入偏好設定");
        resources.GetString("PreferencesMissingTools", CultureInfo.GetCultureInfo("zh-Hant")).Should().Contain("先安裝");
    }

    public void Dispose() => File.Delete(_path);
}
