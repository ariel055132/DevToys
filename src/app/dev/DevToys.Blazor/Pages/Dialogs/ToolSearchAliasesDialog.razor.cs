using DevToys.Blazor.Components;
using DevToys.Business.ViewModels;
using DevToys.Core.Tools;

namespace DevToys.Blazor.Pages.Dialogs;

public partial class ToolSearchAliasesDialog : MefComponentBase
{
    private Dialog _dialog = default!;
    private GuiToolInstance? _loadedTool;
    private bool _isOpen = true;

    [Import]
    internal ToolSearchAliasesViewModel ViewModel { get; set; } = default!;

    [Parameter, EditorRequired]
    public GuiToolInstance Tool { get; set; } = default!;

    [Parameter]
    public EventCallback Closed { get; set; }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (_loadedTool != Tool)
        {
            ViewModel.Load(Tool);
            _loadedTool = Tool;
        }
    }

    private void Save()
    {
        if (ViewModel.Save())
        {
            _dialog.Close();
        }
    }

    private void Cancel()
    {
        _dialog.Close();
    }

    private Task OnClosed()
    {
        _isOpen = false;
        return Closed.InvokeAsync();
    }
}
