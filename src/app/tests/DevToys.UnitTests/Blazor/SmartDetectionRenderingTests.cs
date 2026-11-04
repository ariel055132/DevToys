using System.ComponentModel.Composition;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using DevToys.Blazor.Components;
using DevToys.Blazor.Components.UIElements;
using DevToys.Blazor.Core.Services;
using DevToys.Core.Mef;
using DevToys.Core.Models;
using DevToys.Core.Tools;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace DevToys.UnitTests.Blazor;

public class SmartDetectionRenderingTests : MefBasedTest
{
    [Fact]
    public async Task ToggleDefaultsOnAndPersistsIndependentlyAcrossRenderedPages()
    {
        GuiToolProvider provider = MefProvider.Import<GuiToolProvider>();
        SmartDetectionService service = MefProvider.Import<SmartDetectionService>();
        GuiToolInstance first = provider.GetToolFromInternalName("MockTool")!;
        GuiToolInstance second = provider.GetToolFromInternalName("MockTool2")!;
        await using ServiceProvider services = CreateServices(MefProvider);
        await using var renderer = new InteractionRenderer(services);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            int root = await renderer.OpenToggleAsync(first);
            renderer.Text(root).Should().Contain("Smart recommendations");
            renderer.IsChecked(root).Should().BeTrue();
            await renderer.ToggleAsync(root, false);
            service.GetToolEnabled(first).Should().BeFalse();
            renderer.IsChecked(root).Should().BeFalse();
            renderer.IsChecked(await renderer.OpenToggleAsync(first)).Should().BeFalse();
            renderer.IsChecked(await renderer.OpenToggleAsync(second)).Should().BeTrue();
            await renderer.ToggleAsync(root, true);
            service.GetToolEnabled(first).Should().BeTrue();

            int unsupported = await renderer.OpenToggleAsync(provider.GetToolFromInternalName("MockTool3-XMLFormatter")!);
            renderer.HasInput(unsupported).Should().BeFalse();
        });
    }

    [Fact]
    public async Task OutputSuggestionsRefreshOnToggleAndOldMenuItemsCannotNavigate()
    {
        using var composer = new MefComposer(new[] { typeof(SmartDetectionRenderingTests).Assembly });
        composer.ExportProvider.ComposeExportedValue<IClipboard>(Mock.Of<IClipboard>());
        GuiToolProvider provider = composer.Provider.Import<GuiToolProvider>();
        SmartDetectionService service = composer.Provider.Import<SmartDetectionService>();
        GuiToolInstance first = provider.GetToolFromInternalName("MockTool")!;
        await using ServiceProvider services = CreateServices(composer.Provider);
        await using var renderer = new InteractionRenderer(services);
        var recipient = new object();
        int navigationRequests = 0;
        WeakReferenceMessenger.Default.Register<ChangeSelectedMenuItemMessage>(recipient, (_, _) => navigationRequests++);
        try
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                TestTextInputWrapper wrapper = await renderer.OpenOutputAsync();
                await WaitUntilAsync(() => wrapper.Suggestions.Count == 2);
                var oldItem = wrapper.Suggestions.Single(item => item.SmartDetectedTool.ToolInstance == first);

                service.SetToolEnabled(first, false);
                await WaitUntilAsync(() => wrapper.Suggestions.Count == 1);
                wrapper.Suggestions[0].SmartDetectedTool.ToolInstance.InternalComponentName.Should().Be("MockTool2");
                await oldItem.OnClick.InvokeAsync(oldItem);
                navigationRequests.Should().Be(0);

                service.SetToolEnabled(first, true);
                await WaitUntilAsync(() => wrapper.Suggestions.Count == 2);
                await oldItem.OnClick.InvokeAsync(oldItem);
                navigationRequests.Should().Be(0);
                var newItem = wrapper.Suggestions.Single(item => item.SmartDetectedTool.ToolInstance == first);
                await newItem.OnClick.InvokeAsync(newItem);
                navigationRequests.Should().Be(1);

                service.SetToolEnabled(first, false);
                service.SetToolEnabled(provider.GetToolFromInternalName("MockTool2")!, false);
                await WaitUntilAsync(() => wrapper.HasNoSuggestionsMessage);
                wrapper.HasSuggestions.Should().BeFalse();
            });
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        condition().Should().BeTrue("the rendered recommendations should update");
    }

    private static ServiceProvider CreateServices(IMefProvider mefProvider)
    {
        var js = new Mock<IJSRuntime>();
        js.Setup(runtime => runtime.InvokeAsync<IJSObjectReference>(It.IsAny<string>(), It.IsAny<object[]>()))
            .ReturnsAsync(Mock.Of<IJSObjectReference>());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mefProvider);
        services.AddSingleton(js.Object);
        services.AddSingleton<GlobalDialogService>();
        services.AddSingleton(Mock.Of<IWindowService>());
        services.AddSingleton<ContextMenuService>();
        services.AddSingleton<PopoverService>();
        return services.BuildServiceProvider();
    }

    public sealed class TestTextInputWrapper : UITextInputWrapper
    {
        public IReadOnlyList<SmartDetectionDropDownListItem> Suggestions => SmartDetectionDropDownItems.Where(item => item.SmartDetectedTool is not null).ToArray();
        public bool HasSuggestions => ToolsDetectedBySmartDetection;
        public bool HasNoSuggestionsMessage => SmartDetectionDropDownItems.Any(item => !item.IsEnabled);
    }

#pragma warning disable BL0006 // Dispatch real component events without a browser dependency.
    private sealed class InteractionRenderer : Renderer
    {
        public InteractionRenderer(IServiceProvider services) : base(services, services.GetRequiredService<ILoggerFactory>()) { }
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        public async Task<int> OpenToggleAsync(GuiToolInstance tool)
        {
            int root = AssignRootComponentId(InstantiateComponent(typeof(ToolSmartDetectionToggle)));
            await RenderRootComponentAsync(root, ParameterView.FromDictionary(new Dictionary<string, object> { [nameof(ToolSmartDetectionToggle.Tool)] = tool }));
            return root;
        }

        public async Task<TestTextInputWrapper> OpenOutputAsync()
        {
            var wrapper = (TestTextInputWrapper)InstantiateComponent(typeof(TestTextInputWrapper));
            int root = AssignRootComponentId(wrapper);
            await RenderRootComponentAsync(root, ParameterView.FromDictionary(new Dictionary<string, object>
            {
                [nameof(UITextInputWrapper.UITextInput)] = GUI.SingleLineTextInput().ReadOnly().Text("{ \"json\": 123 }")
            }));
            return wrapper;
        }

        public string Text(int root) => string.Concat(Frames(root).Where(frame => frame.FrameType == RenderTreeFrameType.Text).Select(frame => frame.TextContent));
        public bool HasInput(int root) => Frames(root).Any(frame => frame.FrameType == RenderTreeFrameType.Element && frame.ElementName == "input");
        public bool IsChecked(int root) => Frames(root).Any(frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "checked" && Equals(frame.AttributeValue, true));

        public Task ToggleAsync(int root, bool enabled)
        {
            ulong handler = Frames(root).Single(frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "onchange").AttributeEventHandlerId;
            return DispatchEventAsync(handler, null, new ChangeEventArgs { Value = enabled });
        }

        private IEnumerable<RenderTreeFrame> Frames(int root)
        {
            var frames = GetCurrentRenderTreeFrames(root);
            foreach (RenderTreeFrame frame in frames.Array.Take(frames.Count))
            {
                yield return frame;
                if (frame.FrameType == RenderTreeFrameType.Component)
                {
                    foreach (RenderTreeFrame child in Frames(frame.ComponentId))
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
