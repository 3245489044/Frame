
using ParameterResolver.Models;
using ParameterResolver.Strategies.Abstractions;

namespace WorkFlowMethod.Output.Strategies;

public class TextReadStrategy : IReadStrategy<InputAdapter>
{
    public object? Read(object control, InputAdapter? adapter)
    {
        if (control == null) return null;

        var textProp = control.GetType().GetProperty("Text");
        if (textProp != null)
            return textProp.GetValue(control);

        var valueProp = control.GetType().GetProperty("Value")
                      ?? control.GetType().GetProperty("Checked");
        return valueProp?.GetValue(control);
    }
}
