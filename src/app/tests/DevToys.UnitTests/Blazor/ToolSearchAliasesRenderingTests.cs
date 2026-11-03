using System.Threading.Tasks;
using DevToys.Blazor.Core.Services;
using DevToys.Blazor.Pages.Dialogs;
using DevToys.Business.ViewModels;
using DevToys.Core.Tools;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace DevToys.UnitTests.Blazor;

public class ToolSearchAliasesRenderingTests : MefBasedTest
{
    public ToolSearchAliasesRenderingTests() : base(typeof(ToolSearchAliasesViewModel).Assembly) { }

    [Fact]
    public async Task DialogInputOnlyBecomesSearchableAfterClickingSave()
    {
        GuiToolProvider provider = MefProvider.Import<GuiToolProvider>();
        GuiToolInstance tool = provider.AllTools[0];
        using ServiceProvider services = CreateServices();
        await using var renderer = new InteractionRenderer(services);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            int root = await renderer.OpenAsync(tool);
            renderer.Text(root).Should().Contain("Search aliases").And.Contain(tool.LongOrShortDisplayTitle);
            await renderer.ClickAsync(root, "Add alias");
            await renderer.InputAsync(root, 0, "整理 JSON");
            provider.GetToolSearchAliases(tool).Should().BeEmpty();
            await renderer.ClickAsync(root, "Save");
            provider.GetToolSearchAliases(tool).Should().Equal("整理 JSON");
            services.GetRequiredService<GlobalDialogService>().IsDialogOpened.Should().BeFalse();
        });
    }

    [Fact]
    public async Task DuplicateErrorKeepsDialogOpenAndCancelDiscardsEditsAndRemovals()
    {
        GuiToolProvider provider = MefProvider.Import<GuiToolProvider>();
        GuiToolInstance tool = provider.AllTools[0];
        provider.TrySetToolSearchAliases(tool, new[] { "Tidy", "整理" });
        using ServiceProvider services = CreateServices();
        await using var renderer = new InteractionRenderer(services);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            int root = await renderer.OpenAsync(tool);
            await renderer.InputAsync(root, 1, " tidy ");
            await renderer.ClickAsync(root, "Save");
            renderer.Text(root).Should().Contain("duplicate aliases");
            services.GetRequiredService<GlobalDialogService>().IsDialogOpened.Should().BeTrue();
            provider.GetToolSearchAliases(tool).Should().Equal("Tidy", "整理");

            await renderer.ClickAsync(root, "Remove alias 1");
            await renderer.InputAsync(root, 0, "未儲存別名");
            await renderer.ClickAsync(root, "Cancel");
            services.GetRequiredService<GlobalDialogService>().IsDialogOpened.Should().BeFalse();
            provider.GetToolSearchAliases(tool).Should().Equal("Tidy", "整理");

            int reopened = await renderer.OpenAsync(tool);
            renderer.InputValues(reopened).Should().Equal("Tidy", "整理");
            await renderer.ClickAsync(reopened, "Remove alias 1");
            await renderer.ClickAsync(reopened, "Remove alias 1");
            await renderer.ClickAsync(reopened, "Save");
            provider.GetToolSearchAliases(tool).Should().BeEmpty();
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
        services.AddSingleton<GlobalDialogService>();
        return services.BuildServiceProvider();
    }

    // Dispatch real Blazor event handlers without a browser or a new test framework dependency.
#pragma warning disable BL0006 // RenderTree APIs are used only by this test renderer.
    private sealed class InteractionRenderer : Renderer
    {
        public InteractionRenderer(IServiceProvider services) : base(services, services.GetRequiredService<ILoggerFactory>()) { }

        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        public async Task<int> OpenAsync(GuiToolInstance tool)
        {
            int root = AssignRootComponentId(InstantiateComponent(typeof(ToolSearchAliasesDialog)));
            await RenderRootComponentAsync(root, ParameterView.FromDictionary(new Dictionary<string, object>
            {
                [nameof(ToolSearchAliasesDialog.Tool)] = tool
            }));
            return root;
        }

        public string Text(int root) => string.Concat(Frames(root)
            .Where(frame => frame.FrameType == RenderTreeFrameType.Text).Select(frame => frame.TextContent));

        public IEnumerable<string> InputValues(int root) => Frames(root)
            .Where(frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "value")
            .Select(frame => frame.AttributeValue?.ToString());

        public Task InputAsync(int root, int index, string value)
        {
            ulong handler = Frames(root)
                .Where(frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "oninput")
                .ElementAt(index).AttributeEventHandlerId;
            return DispatchEventAsync(handler, null, new ChangeEventArgs { Value = value });
        }

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
