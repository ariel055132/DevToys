using System.IO;
using System.Text;
using System.Threading.Tasks;
using DevToys.Blazor.BuiltInTools.Settings;
using DevToys.Core.Settings;
using DevToys.Core.Tools;
using DevToys.Core.Tools.ViewItems;

namespace DevToys.UnitTests.Blazor.BuiltInTools;

public class PreferencesTransferServiceTests : MefBasedTest
{
    private readonly ISettingsProvider _settings;
    private readonly GuiToolProvider _tools;
    private readonly SmartDetectionService _detection;
    private readonly PreferencesTransferService _transfer;

    public PreferencesTransferServiceTests()
    {
        _settings = MefProvider.Import<ISettingsProvider>();
        _tools = MefProvider.Import<GuiToolProvider>();
        _detection = MefProvider.Import<SmartDetectionService>();
        _transfer = new PreferencesTransferService(_settings, _tools, _detection);
    }

    [Fact]
    public void ExportImportRoundTripRestoresPreferencesAndLeavesNonPreferencesUntouched()
    {
        GuiToolInstance tool = _tools.AllTools[0];
        _settings.SetSetting(PredefinedSettings.Language, "zh-Hant");
        _settings.SetSetting(PredefinedSettings.Theme, AvailableApplicationTheme.Dark);
        _settings.SetSetting(PredefinedSettings.TextEditorFont, "Menlo");
        _settings.SetSetting(PredefinedSettings.TextEditorEndOfLinePreference, UITextEndOfLinePreference.CRLF);
        _tools.SetToolIsFavorite(tool, true);
        _tools.TrySetToolSearchAliases(tool, new[] { "我的工具", "my tool" });
        _detection.SetToolEnabled(tool, false);
        string backup = _transfer.Export();

        backup.Should().NotContain("RecentTool1").And.NotContain("LastVersionRan").And.NotContain("MainWindow").And.NotContain("IsFirstStart");
        _settings.SetSetting(PredefinedSettings.Theme, AvailableApplicationTheme.Light);
        _settings.SetSetting(PredefinedSettings.Language, "default");
        _tools.SetToolIsFavorite(tool, false);
        _tools.TrySetToolSearchAliases(tool, Array.Empty<string>());
        _detection.SetToolEnabled(tool, true);
        _settings.SetSetting(PredefinedSettings.RecentTool1, "recent-tool");
        _settings.SetSetting(PredefinedSettings.LastVersionRan, "local-version");
        _settings.SetSetting(PredefinedSettings.MainWindowMaximized, true);
        var input = new SettingDefinition<string>("PluginInput", "");
        _settings.SetSetting(input, "private tool input");

        _transfer.PrepareImport(backup).Apply();

        _transfer.Export().Should().Be(backup);
        _settings.GetSetting(PredefinedSettings.RecentTool1).Should().Be("recent-tool");
        _settings.GetSetting(PredefinedSettings.LastVersionRan).Should().Be("local-version");
        _settings.GetSetting(PredefinedSettings.MainWindowMaximized).Should().BeTrue();
        _settings.GetSetting(input).Should().Be("private tool input");
        _transfer.Export().Should().NotContain("private tool input");
    }

    [Fact]
    public void PartialImportMergesOnlyIncludedValuesAndPreparationDoesNotWrite()
    {
        GuiToolInstance tool = _tools.AllTools[0];
        _tools.SetToolIsFavorite(tool, true);
        _settings.SetSetting(PredefinedSettings.TextEditorFont, "Menlo");
        PreferencesImport import = _transfer.PrepareImport(File("\"Theme\":\"Dark\"", "\"MockTool\":{\"searchAliases\":[\"新別名\"]}"));
        import.Count.Should().Be(2);
        _settings.GetSetting(PredefinedSettings.Theme).Should().Be(AvailableApplicationTheme.Default);
        _tools.GetToolSearchAliases(tool).Should().BeEmpty();

        import.Apply();
        _settings.GetSetting(PredefinedSettings.Theme).Should().Be(AvailableApplicationTheme.Dark);
        _settings.GetSetting(PredefinedSettings.TextEditorFont).Should().Be("Menlo");
        _tools.GetToolIsFavorite(tool).Should().BeTrue();
        _tools.GetToolSearchAliases(tool).Should().Equal("新別名");
    }

    [Theory]
    [InlineData("\"CompactMode\":\"true\"", "")]
    [InlineData("\"Theme\":\"Unknown\"", "")]
    [InlineData("\"Theme\":42", "")]
    [InlineData("\"TextEditorEndOfLinePreference\":\"Unknown\"", "")]
    [InlineData("\"Language\":\"not-a-language\"", "")]
    [InlineData("\"Language\":null", "")]
    [InlineData("\"RecentTool1\":\"MockTool\"", "")]
    [InlineData("\"UnknownSetting\":true", "")]
    [InlineData("\"CompactMode\":true,\"CompactMode\":false", "")]
    [InlineData("", "\"MockTool\":{\"searchAliases\":[\"same\",\" SAME \"]}")]
    [InlineData("", "\"MockTool\":{\"searchAliases\":[null]}")]
    [InlineData("", "\"MockTool\":{\"searchAliases\":[\"  \"]}")]
    [InlineData("", "\"MockTool\":{\"searchAliases\":null}")]
    [InlineData("", "\"MockTool\":{\"isFavorite\":1}")]
    [InlineData("", "\"MockTool\":{\"smartDetectionEnabled\":\"false\"}")]
    [InlineData("", "\"MockTool\":{\"input\":\"private\"}")]
    [InlineData("", "\"MockTool\":[],\"MockTool2\":{}")]
    public void InvalidValueRejectsEntireFileWithoutChangingAnyPreferences(string settings, string tools)
    {
        string before = _transfer.Export();
        // Even a valid setting earlier in the file must not be applied.
        string entries = "\"CheckForUpdate\":true" + (settings.Length == 0 ? "" : "," + settings);
        Action prepare = () => _transfer.PrepareImport(File(entries, tools));
        prepare.Should().Throw<FormatException>();
        _transfer.Export().Should().Be(before);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{\"format\":\"DevToys.Preferences\",\"version\":2,\"settings\":{},\"tools\":{}}")]
    [InlineData("{\"format\":\"another app\",\"version\":1,\"settings\":{},\"tools\":{}}")]
    [InlineData("{\"format\":\"DevToys.Preferences\",\"version\":1,\"version\":1,\"settings\":{},\"tools\":{}}")]
    [InlineData("{\"format\":\"DevToys.Preferences\",\"version\":1,\"settings\":{},\"tools\":{}}")]
    public void RejectsInvalidEnvelopeOrEmptyImport(string json)
    {
        Action prepare = () => _transfer.PrepareImport(json);
        prepare.Should().Throw<FormatException>();
    }

    [Fact]
    public void MissingToolRejectsEntireFileAndIdentifiesWhatMustBeInstalled()
    {
        string before = _transfer.Export();
        Action prepare = () => _transfer.PrepareImport(File("\"Theme\":\"Dark\"", "\"Missing.Tool\":{\"isFavorite\":true}"));
        prepare.Should().Throw<MissingPreferenceToolsException>().Which.ToolNames.Should().Equal("Missing.Tool");
        _transfer.Export().Should().Be(before);
    }

    [Fact]
    public void RepeatedImportUpdatesFavoritesMenuWithoutDuplicatesAndNotifiesToolServices()
    {
        var menu = _tools.HeaderAndBodyToolViewItems;
        int aliasesChanged = 0;
        int detectionChanged = 0;
        _tools.SearchAliasesChanged += (_, _) => aliasesChanged++;
        _detection.SettingsChanged += (_, _) => detectionChanged++;
        string json = File("", "\"MockTool\":{\"isFavorite\":true,\"searchAliases\":[\"lookup\"],\"smartDetectionEnabled\":false}");
        _transfer.PrepareImport(json).Apply();
        _transfer.PrepareImport(json).Apply();
        menu.OfType<GroupViewItem>().Single(group => group.InternalName == "FavoriteTools").Children.Should().ContainSingle();
        aliasesChanged.Should().BeGreaterThan(0);
        detectionChanged.Should().Be(1);
        _transfer.PrepareImport(File("", "\"MockTool\":{\"isFavorite\":false}")).Apply();
        menu.OfType<GroupViewItem>().Should().NotContain(group => group.InternalName == "FavoriteTools");
    }

    [Fact]
    public async Task ReadsUtf8BomAndRejectsOversizedAndMalformedStreams()
    {
        byte[] json = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(File("\"Theme\":\"Dark\""))).ToArray();
        using var valid = new MemoryStream(json);
        (await _transfer.ReadImportAsync(valid)).Apply();
        _settings.GetSetting(PredefinedSettings.Theme).Should().Be(AvailableApplicationTheme.Dark);

        using var oversized = new MemoryStream(new byte[PreferencesTransferService.MaximumFileSize + 1]);
        Func<Task> read = async () => await _transfer.ReadImportAsync(oversized);
        await read.Should().ThrowAsync<FormatException>();
        using var invalidEncoding = new MemoryStream(new byte[] { 0xff, 0xff, 0xff });
        read = async () => await _transfer.ReadImportAsync(invalidEncoding);
        await read.Should().ThrowAsync<FormatException>();
    }

    [Fact]
    public void FailedApplyRestoresEarlierValuesAndTheFailingValue()
    {
        var values = new Dictionary<string, object>();
        bool failOnce = true;
        var provider = new Mock<ISettingsProvider>();
        provider.Setup(settings => settings.GetSetting(It.IsAny<SettingDefinition<bool>>()))
            .Returns((SettingDefinition<bool> definition) => values.TryGetValue(definition.Name, out object value) ? (bool)value : definition.DefaultValue);
        provider.Setup(settings => settings.SetSetting(It.IsAny<SettingDefinition<bool>>(), It.IsAny<bool>()))
            .Callback((SettingDefinition<bool> definition, bool value) =>
            {
                values[definition.Name] = value;
                if (definition == PredefinedSettings.CompactMode && failOnce)
                {
                    failOnce = false;
                    throw new IOException("Simulated failure after writing.");
                }
            });
        var transfer = new PreferencesTransferService(provider.Object, _tools, _detection);
        PreferencesImport import = transfer.PrepareImport(File("\"CheckForUpdate\":true,\"CompactMode\":true"));
        Action apply = import.Apply;
        apply.Should().Throw<AggregateException>();
        values[PredefinedSettings.CheckForUpdate.Name].Should().Be(false);
        values[PredefinedSettings.CompactMode.Name].Should().Be(false);
    }

    internal static string File(string settings, string tools = "")
        => "{\"format\":\"DevToys.Preferences\",\"version\":1,\"settings\":{" + settings + "},\"tools\":{" + tools + "}}";
}
