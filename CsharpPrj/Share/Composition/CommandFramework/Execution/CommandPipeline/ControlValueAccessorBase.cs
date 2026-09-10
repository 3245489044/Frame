
namespace CommandFramework.Execution;

public class ControlValueAccessorBase : TConfig.Controls.IControlValueAccessor
{
    private readonly Dictionary<Type, (Func<object, object?> Getter, Action<object, object?> Setter)> _adapters = new();

    private readonly Dictionary<Type, (Func<object, object?> Getter, Func<object, object?, CommandExecutionResult<object>> SetterWithResult)> _adaptersWithResult = new();

    private (Func<object, object?> Getter, Action<object, object?> Setter) RequireAdapter(object controlHandle)
    {
        if (controlHandle == null)
            throw new ArgumentNullException(nameof(controlHandle));

        var adapter = FindAdapter(controlHandle.GetType());
        if (adapter == null)
            throw new NotSupportedException($"Control type '{controlHandle.GetType().Name}' is not supported");

        return adapter.Value;
    }

    public object? GetValue(object controlHandle)
    {
        return RequireAdapter(controlHandle).Getter(controlHandle);
    }

    public void SetValue(object controlHandle, object? value)
    {
        RequireAdapter(controlHandle).Setter(controlHandle, value);
    }

    public TResult? GetValue<TResult>(object controlHandle)
    {
        var value = GetValue(controlHandle);
        if (value == null)
            return default;

        if (value is TResult result)
            return result;

        try
        {
            return (TResult)System.Convert.ChangeType(value, typeof(TResult));
        }
        catch
        {
            return default;
        }
    }

    public bool IsSupported(object controlHandle)
    {
        return controlHandle != null && FindAdapter(controlHandle.GetType()) != null;
    }

    public void RegisterAdapter(Type controlType, Func<object, object?> getter, Action<object, object?> setter)
    {
        ArgumentNullException.ThrowIfNull(controlType);
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);

        _adapters[controlType] = (getter, setter);
    }

    public void RegisterAdapter<TControl>(Func<TControl, object?> getter, Action<TControl, object?> setter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);

        _adapters[typeof(TControl)] = (
            handle => getter((TControl)handle),
            (handle, value) => setter((TControl)handle, value)
        );
    }

    public void RegisterAdapterWithResult<TControl>(
        Func<TControl, object?> getter,
        Func<TControl, object?, CommandExecutionResult<object>> setterWithResult)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setterWithResult);

        _adaptersWithResult[typeof(TControl)] = (
            handle => getter((TControl)handle),
            (handle, value) => setterWithResult((TControl)handle, value)
        );
    }

    public void ClearAdapters()
    {
        _adapters.Clear();
        _adaptersWithResult.Clear();
    }

    private (Func<object, object?> Getter, Action<object, object?> Setter)? FindAdapter(Type controlType)
    {
        if (_adapters.TryGetValue(controlType, out var adapter))
            return adapter;

        var baseType = controlType.BaseType;
        while (baseType != null)
        {
            if (_adapters.TryGetValue(baseType, out adapter))
                return adapter;
            baseType = baseType.BaseType;
        }

        foreach (var iface in controlType.GetInterfaces())
        {
            if (_adapters.TryGetValue(iface, out adapter))
                return adapter;
        }

        return null;
    }
}
