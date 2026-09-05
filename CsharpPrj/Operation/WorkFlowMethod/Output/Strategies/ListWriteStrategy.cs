
using ParameterResolver.Models;
using ParameterResolver.Strategies.Abstractions;
using ParameterResolver.Converter;
using WorkFlowMethod.Diagnostics;

namespace WorkFlowMethod.Output.Strategies;

public class ListWriteStrategy : WriteStrategyBase<OutputAdapter>
{
    protected override string StrategyName => "ListWriteStrategy";
    protected override string LogPrefix => "output.listview.";

    protected override void ExecuteWrite(object control, object value, OutputAdapter? adapter)
    {
        var itemsProp = control.GetType().GetProperty("Items");
        if (itemsProp == null)
        {
            WorkFlowMethodLog.Log("output.listview.items_not_found", null);
            return;
        }

        var items = itemsProp.GetValue(control);
        if (items == null) return;

        var beginUpdateMethod = control.GetType().GetMethod("BeginUpdate");
        var endUpdateMethod = control.GetType().GetMethod("EndUpdate");
        beginUpdateMethod?.Invoke(control, null);

        try
        {
            if (adapter?.ClearBeforeWrite != false)
            {
                var clearMethod = items.GetType().GetMethod("Clear");
                clearMethod?.Invoke(items, null);
            }

            object parseValue = value;
            if (value is not string && value is not System.Collections.IEnumerable)
            {
                parseValue = value.ToString() ?? string.Empty;
            }
            var itemsToAdd = StringToListConverter.Parse(parseValue);

            var itemMethodName = adapter?.ItemMethod ?? "Add";
            var addMethod = items.GetType().GetMethod(itemMethodName, new[] { typeof(string) });
            if (addMethod == null)
            {
                WorkFlowMethodLog.Log("output.listview.addmethod_not_found", new { methodName = itemMethodName });
                return;
            }

            foreach (var itemText in itemsToAdd)
            {
                if (!string.IsNullOrEmpty(itemText))
                    addMethod.Invoke(items, new object[] { itemText });
            }

            WorkFlowMethodLog.Log("output.listview.write_complete", new { itemCount = itemsToAdd.Count });
        }
        finally
        {
            endUpdateMethod?.Invoke(control, null);
        }
    }
}
