using System.Threading;
using System.Threading.Tasks;
using DevToys.Business.ViewModels;
using DevToys.Core.Settings;
using DevToys.Core.Tools;
using DevToys.Core.Tools.Metadata;
using DevToys.Core.Tools.ViewItems;
using DevToys.Core.Version;
using DevToys.Core.Web;

namespace DevToys.UnitTests.Core.Tools;

public class PerToolSmartDetectionTests : MefBasedTest
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TwoMatchingToolsAreEnabledByDefaultAndCanBeDisabledIndependently(bool strict)
    {
        using var scenario = CreateScenario();
        scenario.Service.GetToolEnabled(scenario.First).Should().BeTrue();
        scenario.Service.GetToolEnabled(scenario.Second).Should().BeTrue();
        (await scenario.DetectAsync(strict)).Should().HaveCount(2);

        scenario.Service.SetToolEnabled(scenario.First, false);
        (await scenario.DetectAsync(strict)).Should().ContainSingle().Which.ToolInstance.Should().Be(scenario.Second);
        scenario.Service.SetToolEnabled(scenario.Second, false);
        (await scenario.DetectAsync(strict)).Should().BeEmpty();
        scenario.Service.SetToolEnabled(scenario.First, true);
        (await scenario.DetectAsync(strict)).Should().ContainSingle().Which.ToolInstance.Should().Be(scenario.First);
    }

    [Fact]
    public void SettingSurvivesRecreatingToolsAndService()
    {
        using (var scenario = CreateScenario())
        {
            scenario.Service.SetToolEnabled(scenario.First, false);
        }

        using var reopened = CreateScenario();
        reopened.Service.GetToolEnabled(reopened.First).Should().BeFalse();
        reopened.Service.GetToolEnabled(reopened.Second).Should().BeTrue();
    }

    [Fact]
    public async Task TwoMatchesRecommendBothAndOnlyPasteWhenUserSelectsOne()
    {
        using var scenario = CreateScenario();
        scenario.Provider.SetToolIsFavorite(scenario.First, true);
        scenario.Provider.SetMostRecentUsedTool(scenario.First);
        GroupViewItem group = scenario.Group;
        await scenario.ViewModel.RunSmartDetectionAsync(false, false);

        scenario.ViewModel.SelectedMenuItem.Should().Be(group);
        scenario.Provider.GetViewItemFromTool(scenario.First).Should().OnlyContain(item => item.IsRecommended);
        scenario.Provider.GetViewItemFromTool(scenario.Second).Should().OnlyContain(item => item.IsRecommended);
        scenario.FirstImplementation.Verify(tool => tool.OnDataReceived(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        scenario.SecondImplementation.Verify(tool => tool.OnDataReceived(It.IsAny<string>(), It.IsAny<object>()), Times.Never);

        scenario.Select(scenario.First);
        scenario.FirstImplementation.Verify(tool => tool.OnDataReceived("text", "matching data"), Times.Once);
        scenario.Select(scenario.Second);
        scenario.SecondImplementation.Verify(tool => tool.OnDataReceived(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SingleRemainingMatchHonorsAutomaticPasteSetting(bool paste)
    {
        using var scenario = CreateScenario();
        scenario.Settings.SetSetting(PredefinedSettings.SmartDetectionPaste, paste);
        scenario.Service.SetToolEnabled(scenario.First, false);
        await scenario.ViewModel.RunSmartDetectionAsync(false, false);

        scenario.Provider.GetViewItemFromTool(scenario.First).Should().OnlyContain(item => !item.IsRecommended);
        scenario.Provider.GetViewItemFromTool(scenario.Second).Should().OnlyContain(item => item.IsRecommended);
        if (paste)
        {
            scenario.ViewModel.SelectedMenuItem.Should().BeOfType<GuiToolViewItem>().Which.ToolInstance.Should().Be(scenario.Second);
        }
        else
        {
            scenario.ViewModel.SelectedMenuItem.Should().Be(scenario.Group);
        }
        scenario.FirstImplementation.Verify(tool => tool.OnDataReceived(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        scenario.SecondImplementation.Verify(tool => tool.OnDataReceived("text", "matching data"), paste ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task ToggleClearsOldBadgesAndPendingPasteAndRechecksUnchangedClipboard()
    {
        using var scenario = CreateScenario();
        await scenario.ViewModel.RunSmartDetectionAsync(false, false);
        scenario.Service.SetToolEnabled(scenario.First, false);
        scenario.AllRecommendations().Should().OnlyContain(recommended => !recommended);

        // Neither manual selection can consume the previous two-tool recommendation.
        scenario.Select(scenario.First);
        scenario.Select(scenario.Second);
        scenario.FirstImplementation.Verify(tool => tool.OnDataReceived(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        scenario.SecondImplementation.Verify(tool => tool.OnDataReceived(It.IsAny<string>(), It.IsAny<object>()), Times.Never);

        await scenario.ViewModel.RunSmartDetectionAsync(false, false);
        scenario.SecondImplementation.Verify(tool => tool.OnDataReceived("text", "matching data"), Times.Once);

        scenario.Service.SetToolEnabled(scenario.First, true);
        scenario.AllRecommendations().Should().OnlyContain(recommended => !recommended);
        scenario.ViewModel.SelectedMenuItem = scenario.Group;
        await scenario.ViewModel.RunSmartDetectionAsync(false, false);
        scenario.ViewModel.SelectedMenuItem.Should().Be(scenario.Group);
        scenario.AllRecommendations().Should().OnlyContain(recommended => recommended);
    }

    [Fact]
    public async Task ReenablingAfterNoMatchesRechecksSameClipboard()
    {
        using var scenario = CreateScenario();
        scenario.Service.SetToolEnabled(scenario.First, false);
        scenario.Service.SetToolEnabled(scenario.Second, false);
        await scenario.ViewModel.RunSmartDetectionAsync(false, false);
        scenario.AllRecommendations().Should().OnlyContain(recommended => !recommended);
        scenario.Service.SetToolEnabled(scenario.First, true);
        await scenario.ViewModel.RunSmartDetectionAsync(false, false);
        scenario.FirstImplementation.Verify(tool => tool.OnDataReceived("text", "matching data"), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangingGlobalDetectionOrPasteAlsoInvalidatesPendingData(bool changeDetection)
    {
        using var scenario = CreateScenario();
        await scenario.ViewModel.RunSmartDetectionAsync(false, false);
        scenario.Settings.SetSetting(changeDetection ? PredefinedSettings.SmartDetection : PredefinedSettings.SmartDetectionPaste, false);
        scenario.AllRecommendations().Should().OnlyContain(recommended => !recommended);
        scenario.Select(scenario.First);
        scenario.FirstImplementation.Verify(tool => tool.OnDataReceived(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        await scenario.ViewModel.RunSmartDetectionAsync(false, false);
        scenario.AllRecommendations().Should().OnlyContain(recommended => recommended == !changeDetection);
    }

    [Fact]
    public async Task SettingChangeDiscardsInFlightResultsEvenAfterToolIsReenabled()
    {
        using var scenario = CreateScenario();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<DataDetectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Detector.Setup(detector => detector.TryDetectDataAsync(It.IsAny<object>(), It.IsAny<DataDetectionResult>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                started.TrySetResult();
                return new ValueTask<DataDetectionResult>(result.Task);
            });

        Task detection = scenario.ViewModel.RunSmartDetectionAsync(false, false);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scenario.Service.SetToolEnabled(scenario.First, false);
        scenario.Service.SetToolEnabled(scenario.First, true);
        result.SetResult(new DataDetectionResult(true, "matching data"));
        await detection;
        scenario.AllRecommendations().Should().OnlyContain(recommended => !recommended);
        scenario.Select(scenario.First);
        scenario.FirstImplementation.Verify(tool => tool.OnDataReceived(It.IsAny<string>(), It.IsAny<object>()), Times.Never);

        await scenario.ViewModel.RunSmartDetectionAsync(false, false);
        scenario.AllRecommendations().Should().OnlyContain(recommended => recommended);
    }

    private Scenario CreateScenario()
    {
        ISettingsProvider settings = MefProvider.Import<ISettingsProvider>();
        var first = new Mock<IGuiTool>();
        var second = new Mock<IGuiTool>();
        GuiToolMetadata original = MefProvider.ImportMany<IGuiTool, GuiToolMetadata>().First().Metadata;
        var tools = new[] { first, second }.Select((tool, index) => new Lazy<IGuiTool, GuiToolMetadata>(() => tool.Object,
            new GuiToolMetadata(new Dictionary<string, object>
            {
                [nameof(NameAttribute.InternalComponentName)] = $"RecommendationTool{index}",
                [nameof(ToolDisplayInformationAttribute.IconFontName)] = original.IconFontName,
                [nameof(ToolDisplayInformationAttribute.GroupName)] = original.GroupName,
                [nameof(ToolDisplayInformationAttribute.ShortDisplayTitleResourceName)] = original.ShortDisplayTitleResourceName,
                [nameof(ToolDisplayInformationAttribute.ResourceManagerAssemblyIdentifier)] = original.ResourceManagerAssemblyIdentifier,
                [nameof(ToolDisplayInformationAttribute.ResourceManagerBaseName)] = original.ResourceManagerBaseName,
                [nameof(AcceptedDataTypeNameAttribute.DataTypeName)] = new[] { "text" }
            })));
        var provider = new GuiToolProvider(tools,
            MefProvider.ImportMany<GuiToolGroup, GuiToolGroupMetadata>(),
            MefProvider.ImportMany<IResourceAssemblyIdentifier, ResourceAssemblyIdentifierMetadata>(), settings);
        var detector = new Mock<IDataTypeDetector>();
        detector.Setup(value => value.TryDetectDataAsync(It.IsAny<object>(), It.IsAny<DataDetectionResult>(), It.IsAny<CancellationToken>()))
            .Returns((object data, DataDetectionResult _, CancellationToken _) => new ValueTask<DataDetectionResult>(new DataDetectionResult(true, data)));
        var detectors = new[] { new Lazy<IDataTypeDetector, DataTypeDetectorMetadata>(() => detector.Object,
            new DataTypeDetectorMetadata(new Dictionary<string, object> { [nameof(DataTypeNameAttribute.DataTypeName)] = "text" })) };
        var service = new SmartDetectionService(detectors, settings, provider);
        var clipboard = new Mock<IClipboard>();
        clipboard.Setup(value => value.GetClipboardDataAsync()).ReturnsAsync("matching data");
        var viewModel = new MainWindowViewModel(provider, service, clipboard.Object, settings, Mock.Of<IWebClientService>(), Mock.Of<IVersionService>());
        return new Scenario(provider, service, viewModel, settings, detector, first, second);
    }

    private sealed class Scenario : IDisposable
    {
        public Scenario(GuiToolProvider provider, SmartDetectionService service, MainWindowViewModel viewModel,
            ISettingsProvider settings, Mock<IDataTypeDetector> detector, Mock<IGuiTool> first, Mock<IGuiTool> second)
        {
            Provider = provider;
            Service = service;
            ViewModel = viewModel;
            Settings = settings;
            Detector = detector;
            FirstImplementation = first;
            SecondImplementation = second;
            Group = provider.HeaderAndBodyToolViewItems.OfType<GroupViewItem>().First();
            viewModel.SelectedMenuItem = Group;
        }

        public GuiToolProvider Provider { get; }
        public SmartDetectionService Service { get; }
        public MainWindowViewModel ViewModel { get; }
        public ISettingsProvider Settings { get; }
        public Mock<IDataTypeDetector> Detector { get; }
        public Mock<IGuiTool> FirstImplementation { get; }
        public Mock<IGuiTool> SecondImplementation { get; }
        public GuiToolInstance First => Provider.GetToolFromInternalName("RecommendationTool0")!;
        public GuiToolInstance Second => Provider.GetToolFromInternalName("RecommendationTool1")!;
        public GroupViewItem Group { get; }
        public Task<IReadOnlyList<SmartDetectedTool>> DetectAsync(bool strict) => Service.DetectAsync("matching data", strict, CancellationToken.None);
        public void Select(GuiToolInstance tool) => ViewModel.SelectedMenuItem = Provider.GetViewItemFromTool(tool).First();
        public IEnumerable<bool> AllRecommendations() => Provider.AllTools.SelectMany(Provider.GetViewItemFromTool).Select(item => item.IsRecommended);

        public void Dispose()
        {
            ViewModel.Dispose();
            Service.Dispose();
            Provider.Dispose();
        }
    }
}
