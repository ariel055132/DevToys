using System.Threading.Tasks;
using DevToys.Blazor.Pages.SubPages;
using DevToys.Business.ViewModels;
using DevToys.Core.Tools;
using DevToys.Core.Tools.Metadata;
using DevToys.Core.Tools.ViewItems;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace DevToys.UnitTests.Blazor;

public class FavoriteToolsRenderingTests : MefBasedTest
{
    public FavoriteToolsRenderingTests() : base(typeof(ToolGroupPageViewModel).Assembly) { }

    [Fact]
    public async Task CardActionsReorderAndRemoveFavoritesWithoutOpeningToolsAndRefreshMountedPages()
    {
        GuiToolProvider provider = MefProvider.Import<GuiToolProvider>();
        GuiToolInstance[] tools = { provider.AllTools[0], provider.AllTools[2] };
        foreach (GuiToolInstance tool in tools)
        {
            provider.SetToolIsFavorite(tool, true);
        }
        GroupViewItem favorites = provider.HeaderAndBodyToolViewItems.OfType<GroupViewItem>()
            .Single(group => group.InternalName == ReservedGuiToolGroupNames.FavoriteTools);
        using ServiceProvider services = CreateServices();
        await using var renderer = new InteractionRenderer(services);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            int root = await renderer.OpenAsync(favorites);
            int all = await renderer.OpenAsync((GroupViewItem)provider.HeaderAndBodyToolViewItems[0]);
            await renderer.ClickAsync(root, "Move earlier in favorites: " + tools[1].LongOrShortDisplayTitle);
            provider.GetFavoriteTools().Should().Equal(tools[1], tools[0]);
            string favoritesText = renderer.Text(root);
            favoritesText.IndexOf(tools[1].LongOrShortDisplayTitle, StringComparison.Ordinal)
                .Should().BeLessThan(favoritesText.IndexOf(tools[0].LongOrShortDisplayTitle, StringComparison.Ordinal));
            await renderer.ClickAsync(all, "Move later in favorites: " + tools[1].LongOrShortDisplayTitle);
            provider.GetFavoriteTools().Should().Equal(tools);
            await renderer.ClickAsync(root, "Remove from favorites: " + tools[0].LongOrShortDisplayTitle);
            await renderer.ClickAsync(root, "Remove from favorites: " + tools[1].LongOrShortDisplayTitle);
            renderer.Text(root).Should().Contain("No favorites yet");
            provider.GetMostRecentUsedTools().Should().BeEmpty();
            await renderer.ClickAsync(all, "Add to favorites: " + tools[1].LongOrShortDisplayTitle);
            renderer.Text(root).Should().NotContain("No favorites yet").And.Contain(tools[1].LongOrShortDisplayTitle);
            provider.GetFavoriteTools().Should().Equal(tools[1]);
            provider.GetMostRecentUsedTools().Should().BeEmpty();
        });
    }

    private ServiceProvider CreateServices()
    {
        var js = new Mock<IJSRuntime>();
        js.Setup(runtime => runtime.InvokeAsync<IJSObjectReference>(It.IsAny<string>(), It.IsAny<object[]>()))
            .ReturnsAsync(Mock.Of<IJSObjectReference>());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(MefProvider);
        services.AddSingleton(js.Object);
        return services.BuildServiceProvider();
    }

    // Dispatch real Blazor event handlers without a browser or a new test framework dependency.
#pragma warning disable BL0006 // RenderTree APIs are used only by this test renderer.
    private sealed class InteractionRenderer : Renderer
    {
        public InteractionRenderer(IServiceProvider services) : base(services, services.GetRequiredService<ILoggerFactory>()) { }

        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        public async Task<int> OpenAsync(GroupViewItem group)
        {
            int root = AssignRootComponentId(InstantiateComponent(typeof(ToolGroup)));
            await RenderRootComponentAsync(root, ParameterView.FromDictionary(new Dictionary<string, object>
            {
                [nameof(ToolGroup.GroupViewItem)] = group
            }));
            return root;
        }

        public string Text(int root) => string.Concat(Frames(root)
            .Where(frame => frame.FrameType == RenderTreeFrameType.Text).Select(frame => frame.TextContent));

        public Task ClickAsync(int root, string text)
        {
            foreach (int component in Components(root))
            {
                var frames = GetCurrentRenderTreeFrames(component);
                for (int i = 0; i < frames.Count; i++)
                {
                    RenderTreeFrame frame = frames.Array[i];
                    if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != "button")
                    {
                        continue;
                    }

                    RenderTreeFrame[] button = frames.Array.Skip(i + 1).Take(frame.ElementSubtreeLength - 1).ToArray();
                    if (button.Any(item =>
                        (item.FrameType == RenderTreeFrameType.Text && item.TextContent == text)
                        || (item.FrameType == RenderTreeFrameType.Attribute && item.AttributeName == "aria-label" && Equals(item.AttributeValue, text))))
                    {
                        ulong handler = button.Single(item => item.FrameType == RenderTreeFrameType.Attribute && item.AttributeName == "onclick").AttributeEventHandlerId;
                        return DispatchEventAsync(handler, null, new MouseEventArgs());
                    }
                }
            }
            throw new InvalidOperationException($"Button '{text}' not found.");
        }

        private IEnumerable<RenderTreeFrame> Frames(int root) => Components(root).SelectMany(id =>
        {
            var frames = GetCurrentRenderTreeFrames(id);
            return frames.Array.Take(frames.Count);
        });

        private IEnumerable<int> Components(int root)
        {
            yield return root;
            var frames = GetCurrentRenderTreeFrames(root);
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames.Array[i].FrameType == RenderTreeFrameType.Component)
                {
                    foreach (int child in Components(frames.Array[i].ComponentId))
                    {
                        yield return child;
                    }
                }
            }
        }

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Component rendering failed.", exception);
    }
#pragma warning restore BL0006
}
