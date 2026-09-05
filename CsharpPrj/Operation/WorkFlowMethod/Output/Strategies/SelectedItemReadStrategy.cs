
using ParameterResolver.Models;
using ParameterResolver.Strategies.Abstractions;

namespace WorkFlowMethod.Output.Strategies;

public class SelectedItemReadStrategy : IReadStrategy<InputAdapter>
{
    public object? Read(object control, InputAdapter? adapter)
    {
        if (control == null) return null;

        try
        {
            var selectedItemsProp = control.GetType().GetProperty("SelectedItems");
            if (selectedItemsProp == null) return null;

            var selectedItems = selectedItemsProp.GetValue(control) as System.Collections.IList;
            if (selectedItems == null || selectedItems.Count == 0) return null;

            var firstItem = selectedItems[0];
            if (firstItem == null) return null;

            var valuePath = adapter?.ValuePath ?? "Text";
            var valueProp = firstItem.GetType().GetProperty(valuePath);
            return valueProp?.GetValue(firstItem);
        }
        catch
        {
            return null;
        }
    }
}
