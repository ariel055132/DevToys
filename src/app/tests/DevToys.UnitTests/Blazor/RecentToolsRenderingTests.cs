using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DevToys.Blazor.Pages.SubPages;
using DevToys.Business.ViewModels;
using DevToys.Core.Settings;
using DevToys.Core.Tools;
using DevToys.Core.Tools.ViewItems;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace DevToys.UnitTests.Blazor;

public class RecentToolsRenderingTests : MefBasedTest
{
    public RecentToolsRenderingTests() : base(typeof(ToolGroupPageViewModel).Assembly) { }

    [Fact]
    public async Task MountedAllToolsPageReflectsRemovalClearingAndVisibilityChanges()
    {
        GuiToolProvider provider = MefProvider.Import<GuiToolProvider>();
        ISettingsProvider settings = MefProvider.Import<ISettingsProvider>();
        GuiToolInstance[] tools = provider.AllTools.Take(3).ToArray();
        foreach (GuiToolInstance tool in tools.Reverse())
        {
            provider.SetMostRecentUsedTool(tool);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(MefProvider);
        services.AddSingleton(Mock.Of<IJSRuntime>());
        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(serviceProvider, serviceProvider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = await renderer.RenderComponentAsync<ToolGroup>(ParameterView.FromDictionary(
                new Dictionary<string, object>
                {
                    [nameof(ToolGroup.GroupViewItem)] = (GroupViewItem)provider.HeaderAndBodyToolViewItems[0]
                }));

            CountRemoveButtons(page.ToHtmlString()).Should().Be(3);
            page.ToHtmlString().Should().Contain("Clear recent tools");
            provider.RemoveRecentTool(tools[1]);
            CountRemoveButtons(page.ToHtmlString()).Should().Be(2);

            settings.SetSetting(PredefinedSettings.ShowMostRecentTools, false);
            CountRemoveButtons(page.ToHtmlString()).Should().Be(0);
            settings.SetSetting(PredefinedSettings.ShowMostRecentTools, true);
            CountRemoveButtons(page.ToHtmlString()).Should().Be(2);

            provider.ClearRecentTools();
            string html = page.ToHtmlString();
            CountRemoveButtons(html).Should().Be(0);
            html.Should().NotContain(">Recents</");
            html.Should().NotContain("Clear recent tools");
            Regex.Matches(html, "class=\"tool-group-grid-view-item\"").Count.Should().Be(4);
            provider.ClearRecentTools();
            CountRemoveButtons(page.ToHtmlString()).Should().Be(0);

            provider.SetMostRecentUsedTool(tools[1]);
            CountRemoveButtons(page.ToHtmlString()).Should().Be(1);
            page.ToHtmlString().Should().Contain("Clear recent tools");
        });
    }

    private static int CountRemoveButtons(string html) =>
        Regex.Matches(html, "aria-label=\"Remove from recent tools\"").Count;
}
