using System.IO;
using System.Text.Json;
using DevToys.Business.ViewModels;
using DevToys.Core.Settings;
using DevToys.Core.Tools;
using DevToys.Core.Tools.Metadata;
using DevToys.Core.Tools.ViewItems;
using DevToys.Localization.Strings.MainWindow;

namespace DevToys.UnitTests.Core.Tools;

public class RecentToolsTests : MefBasedTest
{
    private readonly GuiToolProvider _provider;
    private readonly ISettingsProvider _settings;

    public RecentToolsTests() : base(typeof(ToolGroupPageViewModel).Assembly)
    {
        _provider = MefProvider.Import<GuiToolProvider>();
        _settings = MefProvider.Import<ISettingsProvider>();
    }

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(1, 0, 2)]
    [InlineData(2, 0, 1)]
    public void RemoveRecentToolPreservesRemainingOrder(int removed, int first, int second)
    {
        GuiToolInstance[] tools = SeedRecentTools();
        var menu = _provider.HeaderAndBodyToolViewItems;
        var groups = menu.OfType<GroupViewItem>().ToArray();
        GuiToolViewItem retainedItem = menu.OfType<GuiToolViewItem>().First(item => item.ToolInstance == tools[first]);
        int notifications = 0;
        _provider.RecentToolsChanged += (_, _) =>
        {
            notifications++;
            _provider.GetMostRecentUsedTools().Should().Equal(tools[first], tools[second]);
            menu.OfType<GuiToolViewItem>().Select(item => item.ToolInstance).Should().Equal(tools[first], tools[second]);
        };

        _provider.RemoveRecentTool(tools[removed]);

        notifications.Should().Be(1);
        _settings.GetSetting(PredefinedSettings.RecentTool1).Should().Be(tools[first].InternalComponentName);
        _settings.GetSetting(PredefinedSettings.RecentTool2).Should().Be(tools[second].InternalComponentName);
        _settings.GetSetting(PredefinedSettings.RecentTool3).Should().BeEmpty();
        menu.Should().Contain(retainedItem);
        menu.OfType<GroupViewItem>().Should().Equal(groups);
    }

    [Fact]
    public void RemovingMissingToolOrEmptyHistoryDoesNothing()
    {
        int notifications = 0;
        _provider.RecentToolsChanged += (_, _) => notifications++;
        _provider.RemoveRecentTool(_provider.AllTools[0]);
        _provider.GetMostRecentUsedTools().Should().BeEmpty();
        notifications.Should().Be(0);

        GuiToolInstance[] tools = SeedRecentTools();
        notifications = 0;
        _provider.RemoveRecentTool(_provider.AllTools[3]);
        _provider.GetMostRecentUsedTools().Should().Equal(tools);
        notifications.Should().Be(0);
    }

    [Fact]
    public void RemovingLastRecentToolRemovesOnlyRecentSection()
    {
        GuiToolInstance tool = _provider.AllTools[0];
        _provider.SetMostRecentUsedTool(tool);
        var menu = _provider.HeaderAndBodyToolViewItems;

        _provider.RemoveRecentTool(tool);

        _provider.GetMostRecentUsedTools().Should().BeEmpty();
        menu.OfType<GuiToolViewItem>().Should().BeEmpty();
        menu.OfType<SeparatorViewItem>().Should().ContainSingle();
        _provider.SetToolIsFavorite(tool, true);
        ((GroupViewItem)menu[2]).InternalName.Should().Be(ReservedGuiToolGroupNames.FavoriteTools);
        _provider.AllTools.Should().Contain(tool);
    }

    [Fact]
    public void RemovedToolCanReturnAndHistoryStaysLimitedToThree()
    {
        GuiToolInstance[] tools = SeedRecentTools();
        _provider.RemoveRecentTool(tools[1]);
        _provider.SetMostRecentUsedTool(tools[1]);
        _provider.GetMostRecentUsedTools().Should().Equal(tools[1], tools[0], tools[2]);

        _provider.SetMostRecentUsedTool(_provider.AllTools[3]);
        _provider.SetMostRecentUsedTool(tools[0]);
        _provider.SetMostRecentUsedTool(tools[0]);
        _provider.GetMostRecentUsedTools().Should().Equal(tools[0], _provider.AllTools[3], tools[1]);
        _provider.HeaderAndBodyToolViewItems.OfType<GuiToolViewItem>()
            .Select(item => item.ToolInstance).Should().Equal(_provider.GetMostRecentUsedTools());
    }

    [Fact]
    public void UpdatingTheOpenToolPageDoesNotReinsertDeletedHistory()
    {
        GuiToolInstance[] tools = SeedRecentTools();
        GuiToolViewItem selectedItem = _provider.HeaderAndBodyToolViewItems.OfType<GuiToolViewItem>()
            .Single(item => item.ToolInstance == tools[1]);
        ToolPageViewModel page = MefProvider.Import<ToolPageViewModel>();
        page.Load(selectedItem);

        _provider.RemoveRecentTool(tools[1]);
        page.Load(selectedItem);
        _provider.GetMostRecentUsedTools().Should().Equal(tools[0], tools[2]);

        // Navigating away and opening it again is a new use, unlike rerendering the open page.
        page.Load(_provider.GetViewItemFromTool(tools[0]).First());
        page.Load(selectedItem);
        _provider.GetMostRecentUsedTools().Should().Equal(tools[1], tools[0], tools[2]);

        _provider.ClearRecentTools();
        page.Load(selectedItem);
        _provider.GetMostRecentUsedTools().Should().BeEmpty();
    }

    [Fact]
    public void HidingRecentToolsPreservesHistoryAndRemovalWorksWhileHidden()
    {
        GuiToolInstance[] tools = SeedRecentTools();
        var menu = _provider.HeaderAndBodyToolViewItems;
        _settings.SetSetting(PredefinedSettings.ShowMostRecentTools, false);
        _provider.GetMostRecentUsedTools().Should().BeEmpty();
        menu.OfType<GuiToolViewItem>().Should().BeEmpty();
        _settings.GetSetting(PredefinedSettings.RecentTool2).Should().Be(tools[1].InternalComponentName);

        _settings.SetSetting(PredefinedSettings.ShowMostRecentTools, true);
        _provider.GetMostRecentUsedTools().Should().Equal(tools);

        _settings.SetSetting(PredefinedSettings.ShowMostRecentTools, false);
        _provider.RemoveRecentTool(tools[1]);
        _settings.SetSetting(PredefinedSettings.ShowMostRecentTools, true);
        _provider.GetMostRecentUsedTools().Should().Equal(tools[0], tools[2]);
        menu.OfType<GuiToolViewItem>().Select(item => item.ToolInstance).Should().Equal(tools[0], tools[2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearingHistoryPreservesFavoritesToolsSettingsAndVisibility(bool hidden)
    {
        GuiToolInstance[] tools = SeedRecentTools();
        var toolSetting = new SettingDefinition<string>("MockTool.option", "default");
        _settings.SetSetting(toolSetting, "retained");
        _provider.SetToolIsFavorite(tools[1], true);
        _settings.SetSetting(PredefinedSettings.ShowMostRecentTools, !hidden);
        var menu = _provider.HeaderAndBodyToolViewItems;
        GroupViewItem[] groups = menu.OfType<GroupViewItem>().ToArray();
        GuiToolInstance[] allTools = _provider.AllTools.ToArray();
        int notifications = 0;
        _provider.RecentToolsChanged += (_, _) =>
        {
            notifications++;
            _provider.GetMostRecentUsedTools().Should().BeEmpty();
            _settings.GetSetting(PredefinedSettings.RecentTool1).Should().BeEmpty();
            _settings.GetSetting(PredefinedSettings.RecentTool2).Should().BeEmpty();
            _settings.GetSetting(PredefinedSettings.RecentTool3).Should().BeEmpty();
            menu.OfType<GuiToolViewItem>().Should().BeEmpty();
        };

        _provider.ClearRecentTools();

        notifications.Should().Be(1);
        menu.OfType<GroupViewItem>().Should().Equal(groups);
        menu.OfType<SeparatorViewItem>().Should().ContainSingle();
        _provider.AllTools.Should().Equal(allTools);
        _provider.GetToolIsFavorite(tools[1]).Should().BeTrue();
        _settings.GetSetting(toolSetting).Should().Be("retained");
        _settings.GetSetting(PredefinedSettings.ShowMostRecentTools).Should().Be(!hidden);

        _provider.ClearRecentTools();
        notifications.Should().Be(1);
    }

    [Fact]
    public void ClearingEmptyHistoryIsSafeBeforeMenuInitialization()
    {
        int notifications = 0;
        _provider.RecentToolsChanged += (_, _) => notifications++;

        _provider.ClearRecentTools();
        _provider.ClearRecentTools();

        _provider.GetMostRecentUsedTools().Should().BeEmpty();
        _provider.HeaderAndBodyToolViewItems.Should().HaveCount(3);
        notifications.Should().Be(0);
    }

    [Fact]
    public void AllToolsPageReloadsFromTheCompletedChangeNotification()
    {
        GuiToolInstance[] tools = SeedRecentTools();
        ToolGroupPageViewModel page = MefProvider.Import<ToolGroupPageViewModel>();
        var allTools = (GroupViewItem)_provider.HeaderAndBodyToolViewItems[0];
        page.Load(allTools);
        // The page subscribes to this event and reloads on its renderer's dispatcher.
        _provider.RecentToolsChanged += (_, _) => page.Load(allTools);

        _provider.RemoveRecentTool(tools[1]);
        ((ILookup<string, GuiToolInstance>)page.Tools)[MainWindow.RecentToolsDisplayTitle].Should().Equal(tools[0], tools[2]);
        _provider.RemoveRecentTool(tools[0]);
        _provider.RemoveRecentTool(tools[2]);
        ((ILookup<string, GuiToolInstance>)page.Tools).Any(group => group.Key == MainWindow.RecentToolsDisplayTitle).Should().BeFalse();
        ((ILookup<string, GuiToolInstance>)page.Tools).SelectMany(group => group).Should().BeEquivalentTo(_provider.AllTools);
    }

    [Theory]
    [InlineData("remove-middle")]
    [InlineData("remove-last")]
    [InlineData("clear")]
    [InlineData("clear-hidden")]
    [InlineData("clear-empty")]
    public void HistoryChangesSurviveReloadFromDisk(string operation)
    {
        string directory = Path.Combine(Path.GetTempPath(), "DevToys-recent-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "settings.json");
            var settings = new SettingsProvider(new FileSettingsStorage(path));
            string[] expected;
            var toolSetting = new SettingDefinition<string>("MockTool.option", "default");
            using (GuiToolProvider firstSession = CreateProvider(settings))
            {
                GuiToolInstance[] tools = firstSession.AllTools.Take(3).ToArray();
                firstSession.SetToolIsFavorite(tools[1], true);
                settings.SetSetting(toolSetting, "retained");
                IEnumerable<GuiToolInstance> history = operation switch
                {
                    "remove-last" => new[] { tools[1] },
                    "clear-empty" => Array.Empty<GuiToolInstance>(),
                    _ => tools.Reverse()
                };
                foreach (GuiToolInstance tool in history)
                {
                    firstSession.SetMostRecentUsedTool(tool);
                }
                settings.SetSetting(PredefinedSettings.ShowMostRecentTools, operation != "clear-hidden");
                if (operation.StartsWith("clear", StringComparison.Ordinal))
                {
                    firstSession.ClearRecentTools();
                }
                else
                {
                    firstSession.RemoveRecentTool(tools[1]);
                }
                expected = operation == "remove-middle"
                    ? new[] { tools[0].InternalComponentName, tools[2].InternalComponentName }
                    : Array.Empty<string>();
            }

            // New storage, settings provider and tool provider: no shared in-memory history.
            var reopenedSettings = new SettingsProvider(new FileSettingsStorage(path));
            using GuiToolProvider reopened = CreateProvider(reopenedSettings);
            reopenedSettings.GetSetting(PredefinedSettings.ShowMostRecentTools).Should().Be(operation != "clear-hidden");
            reopenedSettings.SetSetting(PredefinedSettings.ShowMostRecentTools, true);
            reopened.GetMostRecentUsedTools().Select(tool => tool.InternalComponentName).Should().Equal(expected);
            reopened.HeaderAndBodyToolViewItems.OfType<GuiToolViewItem>()
                .Select(item => item.ToolInstance.InternalComponentName).Should().Equal(expected);
            reopened.GetToolIsFavorite(reopened.AllTools[1]).Should().BeTrue();
            reopenedSettings.GetSetting(toolSetting).Should().Be("retained");
            reopened.AllTools.Should().HaveCount(4);
            reopened.SetMostRecentUsedTool(reopened.AllTools[1]);
            reopened.GetMostRecentUsedTools().First().Should().Be(reopened.AllTools[1]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private GuiToolInstance[] SeedRecentTools()
    {
        GuiToolInstance[] tools = _provider.AllTools.Take(3).ToArray();
        foreach (GuiToolInstance tool in tools.Reverse())
        {
            _provider.SetMostRecentUsedTool(tool);
        }
        return tools;
    }

    private GuiToolProvider CreateProvider(ISettingsProvider settings) => new(
        MefProvider.ImportMany<IGuiTool, GuiToolMetadata>(),
        MefProvider.ImportMany<GuiToolGroup, GuiToolGroupMetadata>(),
        MefProvider.ImportMany<IResourceAssemblyIdentifier, ResourceAssemblyIdentifierMetadata>(),
        settings);

    // Persists the platform storage contract without touching the user's application settings.
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
