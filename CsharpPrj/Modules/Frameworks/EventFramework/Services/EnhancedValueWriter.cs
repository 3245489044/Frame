
using Modules.EventFramework.Abstractions;
using TConfig;

namespace Modules.EventFramework.Services;

public class EnhancedValueWriter
{
    private readonly IControlFinderStrategy? _controlFinder;
    private readonly IControlAdapterRegistry? _adapterRegistry;
    private readonly IWriteStrategy? _defaultWriteStrategy;
    private readonly IValueFormatter? _formatter;
    private readonly IThreadDispatcher? _dispatcher;

    public EnhancedValueWriter(
        IControlFinderStrategy? controlFinder = null,
        IControlAdapterRegistry? adapterRegistry = null,
        IWriteStrategy? defaultWriteStrategy = null,
        IValueFormatter? formatter = null,
        IThreadDispatcher? dispatcher = null)
    {
        _controlFinder = controlFinder;
        _adapterRegistry = adapterRegistry;
        _defaultWriteStrategy = defaultWriteStrategy;
        _formatter = formatter;
        _dispatcher = dispatcher;
    }

    public bool Write(object sender, string? outputTarget, object? value)
    {
        var target = ResolveTarget(sender, outputTarget);
        if (target == null) return false;

        var adapter = _adapterRegistry?.GetOutputAdapter(target);

        var formattedValue = FormatValue(value);

        return ExecuteWrite(target, formattedValue, adapter);
    }

    public Task<bool> WriteAsync(object sender, string? outputTarget, object? value)
    {
        return Task.Run(() => Write(sender, outputTarget, value));
    }

    private object? ResolveTarget(object sender, string? outputTarget)
    {
        if (string.IsNullOrEmpty(outputTarget))
            return sender;

        if (_controlFinder != null)
            return FindControlViaFinder(sender, outputTarget);

        return FindControlByReflection(sender, outputTarget);
    }

    private object? FindControlViaFinder(object sender, string outputName)
    {
        var parentForm = FindParentForm(sender);
        if (parentForm == null) return null;

        return FindInChildren(parentForm, outputName);
    }

    private object? FindInChildren(object parent, string name)
    {
        if (_controlFinder == null || !_controlFinder.CanHandle(parent)) return null;

        foreach (var child in _controlFinder.GetChildControls(parent))
        {
            var nameProp = child.GetType().GetProperty("Name");
            if (nameProp?.GetValue(child)?.ToString() == name)
                return child;

            var found = FindInChildren(child, name);
            if (found != null) return found;
        }

        return null;
    }

    private string? FormatValue(object? value)
    {
        if (value == null) return string.Empty;

        if (_formatter != null)
        {
            try { return _formatter.Format(value); }
            catch { }
        }

        return value.ToString() ?? string.Empty;
    }

    private bool ExecuteWrite(object target, string? formattedValue, object? adapter)
    {
        try
        {
            if (_dispatcher != null && _dispatcher.NeedsInvoke)
            {
                _dispatcher.Invoke(() => DoWrite(target, formattedValue, adapter));
            }
            else
            {
                DoWrite(target, formattedValue, adapter);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void DoWrite(object target, string? formattedValue, object? adapter)
    {
        if (_defaultWriteStrategy != null)
        {
            _defaultWriteStrategy.Write(target, formattedValue, adapter);
            return;
        }

        var textProp = target.GetType().GetProperty("Text");
        if (textProp != null && textProp.CanWrite)
        {
            textProp.SetValue(target, formattedValue);
        }
    }

    private static object? FindControlByReflection(object sender, string outputName)
    {
        var parentForm = FindParentForm(sender);
        if (parentForm != null)
        {
            var found = FindInContainer(parentForm, outputName);
            if (found != null) return found;
        }

        return FindByParentChain(sender, outputName);
    }

    private static object? FindParentForm(object control)
    {
        try
        {
            var findFormMethod = control.GetType().GetMethod("FindForm");
            return findFormMethod?.Invoke(control, null);
        }
        catch { return null; }
    }

    private static object? FindInContainer(object container, string controlName)
    {
        try
        {
            var controlsProp = container.GetType().GetProperty("Controls");
            if (controlsProp == null) return null;

            var controls = controlsProp.GetValue(container);
            if (controls == null) return null;

            var findMethod = controls.GetType().GetMethod("Find",
                new[] { typeof(string), typeof(bool) });
            if (findMethod == null) return null;

            var result = findMethod.Invoke(controls, new object[] { controlName, true }) as System.Collections.IList;
            return result is { Count: > 0 } ? result[0] : null;
        }
        catch { return null; }
    }

    private static object? FindByParentChain(object sender, string outputName)
    {
        try
        {
            var current = sender;
            while (current != null)
            {
                var parentProp = current.GetType().GetProperty("Parent");
                if (parentProp == null) break;

                var parent = parentProp.GetValue(current);
                if (parent == null) break;

                var found = FindInContainer(parent, outputName);
                if (found != null) return found;

                current = parent;
            }
        }
        catch { }

        return null;
    }
}

public interface IThreadDispatcher
{
    bool NeedsInvoke { get; }

    void Invoke(Action action);
}
