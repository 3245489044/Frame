
using Modules.Threading;
using Share.EventBindingOrchestrator.Adapters;

namespace WorkFlowMethod.EventBinding;

#if !EVENTBINDINGORCHESTRATOR_PROJECT
using IValueAccessor = TConfig.Controls.IControlValueAccessor;
#endif
public class WinFormsValueAccessor : IValueAccessor
{
    private readonly bool _enableCrossThreadDispatching;

    private static readonly AsyncLocal<bool> _isSettingValue = new();

    public WinFormsValueAccessor(bool enableCrossThreadDispatching = true)
    {
        _enableCrossThreadDispatching = enableCrossThreadDispatching;
    }

    public bool CanRead(object control)
    {
        return control != null && control.GetType().GetProperty("Text") != null;
    }

    public bool CanWrite(object control)
    {
        if (control == null) return false;
        var textProp = control.GetType().GetProperty("Text");
        return textProp?.CanWrite == true;
    }

    public object? GetValue(object control)
    {
        if (control == null) return null;

        try
        {
            var textProp = control.GetType().GetProperty("Text");
            return textProp?.GetValue(control);
        }
        catch
        {
            return null;
        }
    }

    public bool IsSupported(object control)
    {
        return CanRead(control);
    }

    public void RegisterAdapter(Type controlType, Func<object, object?> getter, Action<object, object?> setter)
    {
        throw new NotSupportedException("WinFormsValueAccessor 不支持自定义适配器注册。");
    }

    public void ClearAdapters()
    {
        throw new NotSupportedException("WinFormsValueAccessor 不支持自定义适配器注册。");
    }

    public void SetValue(object control, object? value)
    {
        if (control == null) return;

        try
        {
            if (_enableCrossThreadDispatching)
            {
                if (WinFormsThreadDispatcher.Default.TryBeginInvoke(control, () => SetValue(control, value)))
                    return;
            }

            using (ReentryGuard.Enter(control))
            {
                var textProp = control.GetType().GetProperty("Text");
                if (textProp != null && textProp.CanWrite)
                {
                    textProp.SetValue(control, value?.ToString() ?? "");
                }
            }
        }
        catch
        {
        }
    }

}
