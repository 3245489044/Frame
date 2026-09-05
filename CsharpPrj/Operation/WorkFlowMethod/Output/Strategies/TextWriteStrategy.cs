
using ParameterResolver.Models;
using ParameterResolver.Strategies.Abstractions;
using WorkFlowMethod.Diagnostics;

namespace WorkFlowMethod.Output.Strategies;

public class TextWriteStrategy : WriteStrategyBase<OutputAdapter>
{
    protected override string StrategyName => "TextWriteStrategy";
    protected override string LogPrefix => "output.";

    protected override void ExecuteWrite(object control, object value, OutputAdapter? adapter)
    {
        string text;
        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            text = string.Join(", ", enumerable.Cast<object>().Select(x => x?.ToString()));
        }
        else
        {
            text = value.ToString() ?? string.Empty;
        }

        var textProp = control.GetType().GetProperty("Text");
        if (textProp != null && textProp.CanWrite)
        {
            textProp.SetValue(control, text);
            WorkFlowMethodLog.Log("output.written", new { value = text });
        }
        else
        {
            WorkFlowMethodLog.Log("output.not_supported", new { controlType = control.GetType().Name });
        }
    }
}
