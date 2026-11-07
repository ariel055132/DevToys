using System.IO;
using System.Text.Json;
using DevToys.Business.ViewModels;
using DevToys.Core.Settings;
using DevToys.Core.Tools;
using DevToys.Core.Tools.Metadata;
using DevToys.Core.Tools.ViewItems;

namespace DevToys.UnitTests.Core.Tools;

public class FavoriteToolsTests : MefBasedTest
{
    public FavoriteToolsTests() : base(typeof(ToolGroupPageViewModel).Assembly) { }

    [Fact]
    public void LegacyFavoritesKeepToolOrderAndNewFavoritesAppendWithoutDuplicates()
    {
        GuiToolProvider provider = MefProvider.Import<GuiToolProvider>();
        GuiToolInstance[] tools = provider.AllTools.ToArray();
        ISettingsStorage storage = MefProvider.Import<ISettingsStorage>();
        storage.WriteSetting("DevToys.Core." + tools[2].InternalComponentName + "_IsFavorite", true);
        storage.WriteSetting("DevToys.Core." + tools[0].InternalComponentName + "_IsFavorite", true);
        provider.GetFavoriteTools().Should().Equal(tools[0], tools[2]);
        var menu = provider.HeaderAndBodyToolViewItems;
        int notifications = 0;
        provider.FavoriteToolsChanged += (_, _) => notifications++;

        provider.SetToolIsFavorite(tools[1], true);
        provider.SetToolIsFavorite(tools[1], true);
        provider.GetFavoriteTools().Should().Equal(tools[0], tools[2], tools[1]);
        FavoritesGroup(provider).Children.Select(item => item.ToolInstance).Should().Equal(provider.GetFavoriteTools());
        notifications.Should().Be(1);
    }

    [Fact]
    public void MovingFavoritesUpdatesEveryViewAndPreservesSelectionHistoryAndOtherSettings()
    {
        GuiToolProvider provider = MefProvider.Import<GuiToolProvider>();
        GuiToolInstance[] tools = provider.AllTools.Take(3).ToArray();
        foreach (GuiToolInstance tool in tools)
        {
            provider.SetToolIsFavorite(tool, true);
        }
        provider.SetMostRecentUsedTool(tools[1]);
        provider.TrySetToolSearchAliases(tools[1], new[] { "my alias" });
        GroupViewItem group = FavoritesGroup(provider);
        GuiToolViewItem selected = group.Children[2];
        selected.RaiseGotSelected();
        provider.MoveFavoriteTool(tools[2], moveEarlier: true);

        provider.GetFavoriteTools().Should().Equal(tools[0], tools[2], tools[1]);
        group.Children[1].Should().BeSameAs(selected);
        group.GroupShouldBeExpandedInUI.Should().BeTrue();
        provider.GetMostRecentUsedTools().Should().Equal(tools[1]);
        provider.GetToolSearchAliases(tools[1]).Should().Equal("my alias");
        ToolGroupPageViewModel page = MefProvider.Import<ToolGroupPageViewModel>();
        page.Load(group);
        ((ILookup<string, GuiToolInstance>)page.Tools).SelectMany(items => items).Should().Equal(provider.GetFavoriteTools());
        page.Load((GroupViewItem)provider.HeaderAndBodyToolViewItems[0]);
        ((ILookup<string, GuiToolInstance>)page.Tools).Single(items => items.Key == DevToys.Localization.Strings.MainWindow.MainWindow.FavoriteToolsDisplayTitle)
            .Should().Equal(provider.GetFavoriteTools());
    }

    [Fact]
    public void BoundaryMovesAndNonFavoritesAreNoOpsAndLastRemovalCanBeReadded()
    {
        GuiToolProvider provider = MefProvider.Import<GuiToolProvider>();
        GuiToolInstance tool = provider.AllTools[0];
        provider.SetToolIsFavorite(tool, true);
        var menu = provider.HeaderAndBodyToolViewItems;
        int notifications = 0;
        provider.FavoriteToolsChanged += (_, _) => notifications++;
        provider.MoveFavoriteTool(tool, true);
        provider.MoveFavoriteTool(tool, false);
        provider.MoveFavoriteTool(provider.AllTools[1], true);
        notifications.Should().Be(0);
        provider.SetToolIsFavorite(tool, false);
        provider.SetToolIsFavorite(tool, false);
        provider.GetFavoriteTools().Should().BeEmpty();
        menu.OfType<GroupViewItem>().Should().NotContain(group => group.InternalName == ReservedGuiToolGroupNames.FavoriteTools);
        provider.SetToolIsFavorite(tool, true);
        FavoritesGroup(provider).Children.Should().ContainSingle();
        notifications.Should().Be(2);
    }

    [Fact]
    public void SavedOrderToleratesDuplicatesAndMissingExtensionsWithoutLosingTheirPosition()
    {
        GuiToolProvider provider = MefProvider.Import<GuiToolProvider>();
        ISettingsProvider settings = MefProvider.Import<ISettingsProvider>();
        GuiToolInstance[] tools = provider.AllTools.Take(2).ToArray();
        foreach (GuiToolInstance tool in tools)
        {
            provider.SetToolIsFavorite(tool, true);
        }
        settings.SetSetting(PredefinedSettings.FavoriteToolOrder,
            new[] { tools[1].InternalComponentName, "missing-extension", "", tools[1].InternalComponentName, tools[0].InternalComponentName });
        provider.GetFavoriteTools().Should().Equal(tools[1], tools[0]);
        provider.MoveFavoriteTool(tools[0], true);
        settings.GetSetting(PredefinedSettings.FavoriteToolOrder)
            .Should().Equal(tools[0].InternalComponentName, "missing-extension", tools[1].InternalComponentName);
    }

    [Fact]
    public void ReorderingAndRemovingFavoritesSurviveReloadFromDisk()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DevToys-favorites-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "settings.json");
            string[] expected;
            using (GuiToolProvider first = CreateProvider(new SettingsProvider(new FileSettingsStorage(path))))
            {
                GuiToolInstance[] tools = first.AllTools.Take(3).ToArray();
                foreach (GuiToolInstance tool in tools)
                {
                    first.SetToolIsFavorite(tool, true);
                }
                first.MoveFavoriteTool(tools[2], true);
                first.MoveFavoriteTool(tools[2], true);
                first.SetToolIsFavorite(tools[0], false);
                first.SetToolIsFavorite(tools[0], true);
                expected = new[] { tools[2].InternalComponentName, tools[1].InternalComponentName, tools[0].InternalComponentName };
            }
            using GuiToolProvider reopened = CreateProvider(new SettingsProvider(new FileSettingsStorage(path)));
            reopened.GetFavoriteTools().Select(tool => tool.InternalComponentName).Should().Equal(expected);
            FavoritesGroup(reopened).Children.Select(item => item.ToolInstance.InternalComponentName).Should().Equal(expected);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static GroupViewItem FavoritesGroup(GuiToolProvider provider) => provider.HeaderAndBodyToolViewItems
        .OfType<GroupViewItem>().Single(group => group.InternalName == ReservedGuiToolGroupNames.FavoriteTools);

    private GuiToolProvider CreateProvider(ISettingsProvider settings) => new(
        MefProvider.ImportMany<IGuiTool, GuiToolMetadata>(),
        MefProvider.ImportMany<GuiToolGroup, GuiToolGroupMetadata>(),
        MefProvider.ImportMany<IResourceAssemblyIdentifier, ResourceAssemblyIdentifierMetadata>(), settings);

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
