using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using DevToys.Business.ViewModels;
using DevToys.Core.Settings;
using DevToys.Core.Tools;
using DevToys.Core.Tools.Metadata;
using DevToys.Core.Tools.ViewItems;

namespace DevToys.UnitTests.Core.Tools;

public class ToolSearchAliasesTests : MefBasedTest
{
    private readonly GuiToolProvider _provider;

    public ToolSearchAliasesTests() : base(typeof(ToolSearchAliasesViewModel).Assembly)
    {
        _provider = MefProvider.Import<GuiToolProvider>();
    }

    [Theory]
    [InlineData("整理 JSON")]
    [InlineData("整理")]
    [InlineData("理 JSO")]
    [InlineData("tidy")]
    [InlineData("TIDY")]
    [InlineData("idy")]
    [InlineData("  整理 JSON  ")]
    public void SavedAliasesSupportChineseEnglishAndSubstrings(string query)
    {
        GuiToolInstance tool = _provider.AllTools[0];
        _provider.TrySetToolSearchAliases(tool, new[] { " 整理 JSON ", "Tidy" }).Should().Be(ToolSearchAliasValidationResult.Valid);
        _provider.GetToolSearchAliases(tool).Should().Equal("整理 JSON", "Tidy");
        GuiToolViewItem result = Search(_provider, query).Should().ContainSingle().Subject;
        result.ToolInstance.Should().Be(tool);
        result.MenuDisplayTitle.Should().Be(tool.LongOrShortDisplayTitle);
        result.MatchedSpans.Should().BeEmpty();
    }

    [Theory]
    [InlineData("JSON", " json ")]
    [InlineData("整理", " 整理 ")]
    public void DuplicateAliasesAreRejectedWithoutChangingSavedAliases(string first, string duplicate)
    {
        GuiToolInstance tool = _provider.AllTools[0];
        _provider.TrySetToolSearchAliases(tool, new[] { "原本別名" });
        int notifications = 0;
        _provider.SearchAliasesChanged += (_, _) => notifications++;

        _provider.TrySetToolSearchAliases(tool, new[] { first, duplicate }).Should().Be(ToolSearchAliasValidationResult.DuplicateAlias);
        _provider.GetToolSearchAliases(tool).Should().Equal("原本別名");
        notifications.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public void BlankAliasesAreRejectedAtomically(string blank)
    {
        GuiToolInstance tool = _provider.AllTools[0];
        _provider.TrySetToolSearchAliases(tool, new[] { "原本別名" });
        _provider.TrySetToolSearchAliases(tool, new[] { "新的別名", blank }).Should().Be(ToolSearchAliasValidationResult.EmptyAlias);
        _provider.GetToolSearchAliases(tool).Should().Equal("原本別名");
    }

    [Fact]
    public void ExactAliasesOutrankStrongTitleMatchesAndBypassScoreThreshold()
    {
        GuiToolInstance tool = _provider.GetToolFromInternalName("MockTool")!;
        _provider.TrySetToolSearchAliases(tool, new[] { "XML Validator" });

        var results = Search(_provider, "xml validator");
        results[0].ToolInstance.Should().Be(tool);
        results.Should().Contain(result => result.ToolInstance.InternalComponentName == "MockTool4-XMLValidator");
    }

    [Fact]
    public void SharedAliasesReturnBothToolsOnceEvenWithMultipleMatchingAliases()
    {
        GuiToolInstance[] tools = _provider.AllTools.Take(2).ToArray();
        _provider.TrySetToolSearchAliases(tools[0], new[] { "整理", "整理資料", "整理文字" });
        _provider.TrySetToolSearchAliases(tools[1], new[] { "整理" });

        Search(_provider, "整理").Select(result => result.ToolInstance).Should().Equal(tools);
    }

    [Fact]
    public void SharedAliasesRespectFiveResultLimitAndNonSearchableTools()
    {
        Lazy<IGuiTool, GuiToolMetadata> original = MefProvider.ImportMany<IGuiTool, GuiToolMetadata>().First();
        var definitions = Enumerable.Range(0, 8).Select(index => new Lazy<IGuiTool, GuiToolMetadata>(
            () => original.Value,
            new GuiToolMetadata(new Dictionary<string, object>
            {
                [nameof(NameAttribute.InternalComponentName)] = $"AliasTool{index}",
                [nameof(ToolDisplayInformationAttribute.IconFontName)] = original.Metadata.IconFontName,
                [nameof(ToolDisplayInformationAttribute.GroupName)] = original.Metadata.GroupName,
                [nameof(ToolDisplayInformationAttribute.ShortDisplayTitleResourceName)] = original.Metadata.ShortDisplayTitleResourceName,
                [nameof(ToolDisplayInformationAttribute.ResourceManagerAssemblyIdentifier)] = original.Metadata.ResourceManagerAssemblyIdentifier,
                [nameof(ToolDisplayInformationAttribute.ResourceManagerBaseName)] = original.Metadata.ResourceManagerBaseName,
                [nameof(NotSearchableAttribute.NotSearchable)] = index == 0
            })));
        using var provider = new GuiToolProvider(
            definitions,
            MefProvider.ImportMany<GuiToolGroup, GuiToolGroupMetadata>(),
            MefProvider.ImportMany<IResourceAssemblyIdentifier, ResourceAssemblyIdentifierMetadata>(),
            MefProvider.Import<ISettingsProvider>());
        foreach (GuiToolInstance tool in provider.AllTools)
        {
            provider.TrySetToolSearchAliases(tool, new[] { "共用別名" });
        }

        var results = Search(provider, "共用別名");
        results.Should().HaveCount(5);
        results.Select(result => result.ToolInstance).Should().Equal(provider.AllTools.Where(tool => !tool.NotSearchable).Take(5));
        provider.SearchTools(" \t ", results);
        results.Should().BeEmpty();
    }

    [Fact]
    public void SaveUpdatesExistingResultsAndLeavesFavoritesHistoryAndToolSettingsIntact()
    {
        GuiToolInstance tool = _provider.AllTools[0];
        ISettingsProvider settings = MefProvider.Import<ISettingsProvider>();
        var option = new SettingDefinition<string>("MockTool.Option", "default");
        settings.SetSetting(option, "retained");
        _provider.SetToolIsFavorite(tool, true);
        _provider.SetMostRecentUsedTool(tool);
        var results = Search(_provider, "整理");
        results.Should().Equal(GuiToolProvider.NoResultFoundItem);
        _provider.SearchAliasesChanged += (_, _) => _provider.SearchTools("整理", results);

        _provider.TrySetToolSearchAliases(tool, new[] { "整理" });
        results.Should().ContainSingle().Which.ToolInstance.Should().Be(tool);
        _provider.TrySetToolSearchAliases(tool, new[] { "轉換" });
        results.Should().Equal(GuiToolProvider.NoResultFoundItem);
        Search(_provider, "轉換").Should().ContainSingle().Which.ToolInstance.Should().Be(tool);
        _provider.TrySetToolSearchAliases(tool, Array.Empty<string>());
        Search(_provider, "轉換").Should().Equal(GuiToolProvider.NoResultFoundItem);

        _provider.GetToolIsFavorite(tool).Should().BeTrue();
        _provider.GetMostRecentUsedTools().Should().Equal(tool);
        settings.GetSetting(option).Should().Be("retained");
        Search(_provider, "XML Validator")[0].ToolInstance.InternalComponentName.Should().Be("MockTool4-XMLValidator");
    }

    [Theory]
    [InlineData("add")]
    [InlineData("edit")]
    [InlineData("delete")]
    public void SavedChangesSurviveNewStorageAndProviders(string operation)
    {
        string path = Path.Combine(Path.GetTempPath(), "DevToys-aliases-" + Guid.NewGuid() + ".json");
        try
        {
            string toolName;
            string[] expected = operation == "delete" ? Array.Empty<string>() : new[] { "整理 JSON", "Tidy" };
            using (GuiToolProvider first = CreateProvider(new SettingsProvider(new FileSettingsStorage(path))))
            {
                GuiToolInstance tool = first.AllTools[0];
                toolName = tool.InternalComponentName;
                if (operation != "add")
                {
                    first.TrySetToolSearchAliases(tool, new[] { "舊別名" });
                }
                first.TrySetToolSearchAliases(tool, expected);
            }

            using GuiToolProvider reopened = CreateProvider(new SettingsProvider(new FileSettingsStorage(path)));
            GuiToolInstance reopenedTool = reopened.GetToolFromInternalName(toolName)!;
            reopened.GetToolSearchAliases(reopenedTool).Should().Equal(expected);
            Search(reopened, "舊別名").Should().Equal(GuiToolProvider.NoResultFoundItem);
            if (expected.Length > 0)
            {
                Search(reopened, "整理 JSON").Should().ContainSingle().Which.ToolInstance.Should().Be(reopenedTool);
            }
            else
            {
                Search(reopened, "整理 JSON").Should().Equal(GuiToolProvider.NoResultFoundItem);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DraftAddEditAndDeleteHaveNoEffectUntilSaveAndReloadDiscardsThem()
    {
        GuiToolInstance tool = _provider.AllTools[0];
        _provider.TrySetToolSearchAliases(tool, new[] { "原本別名", "保留別名" });
        var editor = new ToolSearchAliasesViewModel(_provider);
        editor.Load(tool);
        editor.Update(editor.Aliases[0], "新的別名");
        editor.Remove(editor.Aliases[1]);
        editor.Add();
        editor.Update(editor.Aliases[1], "新增別名");

        _provider.GetToolSearchAliases(tool).Should().Equal("原本別名", "保留別名");
        Search(_provider, "新增別名").Should().Equal(GuiToolProvider.NoResultFoundItem);
        editor.Load(tool); // Reopening after Cancel creates a fresh draft from saved settings.
        editor.Aliases.Select(entry => entry.Text).Should().Equal("原本別名", "保留別名");

        editor.Update(editor.Aliases[0], "新的別名");
        editor.Save().Should().BeTrue();
        _provider.GetToolSearchAliases(tool).Should().Equal("新的別名", "保留別名");
        Search(_provider, "新的別名").Should().ContainSingle().Which.ToolInstance.Should().Be(tool);
    }

    [Fact]
    public void InvalidDraftRemainsEditableAndCannotOverwriteSavedAliases()
    {
        GuiToolInstance tool = _provider.AllTools[0];
        _provider.TrySetToolSearchAliases(tool, new[] { "Tidy" });
        var editor = new ToolSearchAliasesViewModel(_provider);
        editor.Load(tool);
        editor.Add();
        editor.Save().Should().BeFalse();
        editor.Error.Should().NotBeNullOrWhiteSpace();
        editor.Update(editor.Aliases[1], " tidy ");
        editor.Save().Should().BeFalse();
        editor.Error.Should().NotBeNullOrWhiteSpace();
        _provider.GetToolSearchAliases(tool).Should().Equal("Tidy");
        editor.Update(editor.Aliases[1], "整理");
        editor.Save().Should().BeTrue();
        editor.Error.Should().BeNull();
        _provider.GetToolSearchAliases(tool).Should().Equal("Tidy", "整理");
    }

    private static ObservableCollection<GuiToolViewItem> Search(GuiToolProvider provider, string query)
    {
        var results = new ObservableCollection<GuiToolViewItem>();
        provider.SearchTools(query, results);
        return results;
    }

    private GuiToolProvider CreateProvider(ISettingsProvider settings) => new(
        MefProvider.ImportMany<IGuiTool, GuiToolMetadata>(),
        MefProvider.ImportMany<GuiToolGroup, GuiToolGroupMetadata>(),
        MefProvider.ImportMany<IResourceAssemblyIdentifier, ResourceAssemblyIdentifierMetadata>(),
        settings);

    // Exercise the platform storage contract with actual disk writes, without touching user settings.
    private sealed class FileSettingsStorage : ISettingsStorage
    {
        private readonly string _path;
        private readonly Dictionary<string, string> _values;

        public FileSettingsStorage(string path)
        {
            _path = path;
            _values = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!
                : new Dictionary<string, string>();
        }

        public bool TryReadSetting(string settingName, out object value)
        {
            bool found = _values.TryGetValue(settingName, out string result);
            value = result;
            return found;
        }

        public void WriteSetting(string settingName, object value)
        {
            _values[settingName] = value?.ToString() ?? string.Empty;
            File.WriteAllText(_path, JsonSerializer.Serialize(_values));
        }

        public void ResetSetting(string settingName)
        {
            _values.Remove(settingName);
            File.WriteAllText(_path, JsonSerializer.Serialize(_values));
        }
    }
}
