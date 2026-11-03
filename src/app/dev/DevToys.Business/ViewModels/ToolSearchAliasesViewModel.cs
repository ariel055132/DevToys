using DevToys.Core.Tools;
using DevToys.Localization.Strings.ToolPage;

namespace DevToys.Business.ViewModels;

[Export]
[PartCreationPolicy(CreationPolicy.NonShared)]
internal sealed class ToolSearchAliasesViewModel
{
    private readonly GuiToolProvider _provider;
    private GuiToolInstance? _tool;

    [ImportingConstructor]
    public ToolSearchAliasesViewModel(GuiToolProvider provider)
    {
        _provider = provider;
    }

    internal List<AliasEntry> Aliases { get; } = new();

    internal string? Error { get; private set; }

    internal void Load(GuiToolInstance tool)
    {
        _tool = tool;
        Aliases.Clear();
        Aliases.AddRange(_provider.GetToolSearchAliases(tool).Select(alias => new AliasEntry { Text = alias }));
        Error = null;
    }

    internal void Add()
    {
        Aliases.Add(new AliasEntry());
        Error = null;
    }

    internal void Remove(AliasEntry entry)
    {
        Aliases.Remove(entry);
        Error = null;
    }

    internal void Update(AliasEntry entry, string text)
    {
        entry.Text = text;
        Error = null;
    }

    internal bool Save()
    {
        Guard.IsNotNull(_tool);
        Error = _provider.TrySetToolSearchAliases(_tool, Aliases.Select(alias => alias.Text)) switch
        {
            ToolSearchAliasValidationResult.EmptyAlias => ToolPage.EmptySearchAliasError,
            ToolSearchAliasValidationResult.DuplicateAlias => ToolPage.DuplicateSearchAliasError,
            _ => null
        };
        return Error is null;
    }

    internal sealed class AliasEntry
    {
        internal string Text { get; set; } = string.Empty;
    }
}
