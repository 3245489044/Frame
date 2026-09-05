#nullable enable

namespace FrameworkAbstractions.Controls;

public interface IListSelectionAdapter
{
    int GetSelectedCount(object control);

    void ClearSelection(object control);

    bool IsSelected(object control, int index);

    void SelectItem(object control, int index);

    int GetItemCount(object control);
}