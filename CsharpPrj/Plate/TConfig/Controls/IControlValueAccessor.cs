
namespace TConfig.Controls;

public interface IControlValueAccessor
{
    object? GetValue(object control);

    void SetValue(object control, object? value);

    bool IsSupported(object control);

    void RegisterAdapter(Type controlType, Func<object, object?> getter, Action<object, object?> setter);

    void ClearAdapters();
}
