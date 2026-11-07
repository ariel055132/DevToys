using System.Collections.ObjectModel;
using DevToys.Core.Tools.ViewItems;
using PredefinedSettings = DevToys.Core.Settings.PredefinedSettings;

namespace DevToys.Core.Tools;

public sealed partial class GuiToolProvider
{
    /// <summary>
    /// Gets favorites in their saved order, followed by legacy favorites in tool order.
    /// </summary>
    public IReadOnlyList<GuiToolInstance> GetFavoriteTools()
    {
        var remaining = AllTools.Where(tool => !tool.NotFavorable && GetToolIsFavorite(tool))
            .ToDictionary(tool => tool.InternalComponentName, StringComparer.Ordinal);
        var result = new List<GuiToolInstance>();
        foreach (string name in GetSavedFavoriteOrder())
        {
            if (remaining.Remove(name, out GuiToolInstance? tool))
            {
                result.Add(tool);
            }
        }
        result.AddRange(AllTools.Where(tool => remaining.ContainsKey(tool.InternalComponentName)));
        return result;
    }

    /// <summary>
    /// Adds a new favorite at the end, or removes it. Repeated writes do not duplicate items.
    /// </summary>
    public void SetToolIsFavorite(GuiToolInstance guiToolInstance, bool isFavorite)
    {
        Guard.IsNotNull(guiToolInstance);
        if (guiToolInstance.NotFavorable || GetToolIsFavorite(guiToolInstance) == isFavorite)
        {
            return;
        }

        List<string> order = GetSavedFavoriteOrder();
        foreach (GuiToolInstance favorite in GetFavoriteTools())
        {
            if (!order.Contains(favorite.InternalComponentName, StringComparer.Ordinal))
            {
                order.Add(favorite.InternalComponentName);
            }
        }
        if (isFavorite)
        {
            if (!order.Contains(guiToolInstance.InternalComponentName, StringComparer.Ordinal))
            {
                order.Add(guiToolInstance.InternalComponentName);
            }
        }
        else
        {
            order.Remove(guiToolInstance.InternalComponentName);
        }

        _settingsProvider.SetSetting(CreateIsToolFavoriteSettingDefinition(guiToolInstance), isFavorite);
        _settingsProvider.SetSetting(PredefinedSettings.FavoriteToolOrder, order.ToArray());
    }

    public bool GetToolIsFavorite(GuiToolInstance guiToolInstance)
    {
        Guard.IsNotNull(guiToolInstance);
        return _settingsProvider.GetSetting(CreateIsToolFavoriteSettingDefinition(guiToolInstance));
    }

    /// <summary>
    /// Moves a favorite one position earlier or later among available favorites.
    /// </summary>
    public void MoveFavoriteTool(GuiToolInstance tool, bool moveEarlier)
    {
        Guard.IsNotNull(tool);
        var favorites = GetFavoriteTools().ToList();
        int index = favorites.IndexOf(tool);
        int destination = index + (moveEarlier ? -1 : 1);
        if (index < 0 || destination < 0 || destination >= favorites.Count)
        {
            return;
        }

        List<string> order = GetSavedFavoriteOrder();
        foreach (GuiToolInstance favorite in favorites)
        {
            if (!order.Contains(favorite.InternalComponentName, StringComparer.Ordinal))
            {
                order.Add(favorite.InternalComponentName);
            }
        }
        int first = order.IndexOf(tool.InternalComponentName);
        int second = order.IndexOf(favorites[destination].InternalComponentName);
        (order[first], order[second]) = (order[second], order[first]);
        _settingsProvider.SetSetting(PredefinedSettings.FavoriteToolOrder, order.ToArray());
    }

    private List<string> GetSavedFavoriteOrder() =>
        (_settingsProvider.GetSetting(PredefinedSettings.FavoriteToolOrder) ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).ToList();

    private void OnFavoriteToolsChanged()
    {
        if (_headerAndBodyToolViewItems is not null)
        {
            IReadOnlyList<GuiToolInstance> favorites = GetFavoriteTools();
            if (_favoriteToolsGroupViewItem is null && favorites.Count > 0)
            {
                CreateFavoriteGroupViewItem(Array.Empty<GuiToolViewItem>());
            }
            if (_favoriteToolsGroupViewItem is not null)
            {
                // Retain item identities so reordering keeps selection and expansion state.
                ObservableCollection<GuiToolViewItem> children = _favoriteToolsGroupViewItem.Children!;
                foreach (GuiToolViewItem item in children.ToArray())
                {
                    if (!favorites.Contains(item.ToolInstance))
                    {
                        children.Remove(item);
                    }
                }
                for (int i = 0; i < favorites.Count; i++)
                {
                    GuiToolViewItem? item = children.FirstOrDefault(child => child.ToolInstance == favorites[i]);
                    if (item is null)
                    {
                        children.Insert(i, new GuiToolViewItem(favorites[i]));
                    }
                    else if (children.IndexOf(item) != i)
                    {
                        children.Move(children.IndexOf(item), i);
                    }
                }
                if (favorites.Count == 0)
                {
                    _headerAndBodyToolViewItems.Remove(_favoriteToolsGroupViewItem);
                }
                else if (!_headerAndBodyToolViewItems.Contains(_favoriteToolsGroupViewItem))
                {
                    int index = _headerAndBodyToolViewItems.IndexOf(_separatorAfterRecentTools ?? _separatorAfterAllToolsItem) + 1;
                    _headerAndBodyToolViewItems.Insert(index, _favoriteToolsGroupViewItem);
                }
            }
        }
        FavoriteToolsChanged?.Invoke(this, EventArgs.Empty);
    }
}
