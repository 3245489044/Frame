
using System.Collections;
using System.Reflection;
using Modules.EventFramework.Abstractions;
using Modules.EventFramework.Core;

namespace Modules.EventFramework.Adapters;

public class WinFormsEventSourceNode : EventSourceNodeBase
{
    private readonly object _control;
    private readonly Lazy<Dictionary<string, EventInfo>> _eventCache;

    public override string Name => GetControlName();

    public override string SourceType => _control.GetType().Name;

    public override bool CanRead => _control.GetType().GetProperty("Text") != null;

    public override bool CanWrite => _control.GetType().GetProperty("Text")?.CanWrite == true;

    public override bool HasChildren => GetChildControls().Any();

    public WinFormsEventSourceNode(object control)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _eventCache = new Lazy<Dictionary<string, EventInfo>>(() =>
        {
            return _control.GetType()
                .GetEvents(BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        });
    }

    protected override bool OnSubscribe(string eventName, Delegate handler)
    {
        if (_eventCache.Value.TryGetValue(eventName, out var eventInfo))
        {
            eventInfo.AddEventHandler(_control, handler);
            return true;
        }
        return false;
    }

    protected override bool OnUnsubscribe(string eventName, Delegate handler)
    {
        if (_eventCache.Value.TryGetValue(eventName, out var eventInfo))
        {
            eventInfo.RemoveEventHandler(_control, handler);
            return true;
        }
        return false;
    }

    public override object? GetValue()
    {
        var textProp = _control.GetType().GetProperty("Text");
        return textProp?.GetValue(_control);
    }

    public override void SetValue(object? value)
    {
        var textProp = _control.GetType().GetProperty("Text");
        if (textProp?.CanWrite == true)
            textProp.SetValue(_control, value?.ToString() ?? "");
    }

    protected override IEnumerable<IEventSourceNode> GetChildrenCore()
    {
        foreach (var child in GetChildControls())
            yield return new WinFormsEventSourceNode(child);
    }

    protected override IEventSourceNode? FindChildCore(string name)
    {
        return FindChildRecursive(this, name);
    }

    public override IEnumerable<string> GetSupportedEvents()
    {
        return _eventCache.Value.Keys;
    }

    private string GetControlName()
    {
        return GetControlName(_control);
    }

    public static string GetControlName(object control)
    {
        try
        {
            var nameProp = control.GetType().GetProperty("Name");
            if (nameProp != null)
            {
                var name = nameProp.GetValue(control)?.ToString();
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
        }
        catch
        {
        }
        return control.GetType().Name;
    }

    private IEnumerable<object> GetChildControls()
    {
        var controlsProp = _control.GetType().GetProperty("Controls");
        if (controlsProp?.GetValue(_control) is IEnumerable controls)
        {
            foreach (var child in controls)
                yield return child;
        }
    }

    private static IEventSourceNode? FindChildRecursive(WinFormsEventSourceNode parent, string name)
    {
        foreach (var child in parent.GetChildren())
        {
            var source = (IEventSource)child;
            if (string.Equals(source.Name, name, StringComparison.OrdinalIgnoreCase))
                return child;
            if (child is WinFormsEventSourceNode winChild && winChild.HasChildren)
            {
                var found = FindChildRecursive(winChild, name);
                if (found != null) return found;
            }
        }
        return null;
    }

    public static object? FindControlByName(object form, string controlName)
    {
        try
        {
            var controlsProp = form.GetType().GetProperty("Controls");
            if (controlsProp == null) return null;
            var controls = controlsProp.GetValue(form);
            if (controls == null) return null;
            var findMethod = controls.GetType().GetMethod("Find");
            if (findMethod == null) return null;
            var result = findMethod.Invoke(controls, new object[] { controlName, true }) as Array;
            if (result != null && result.Length > 0)
                return result.GetValue(0);
        }
        catch
        {
        }
        return null;
    }

    public static string GetControlValue(object control)
    {
        try
        {
            var textProp = control.GetType().GetProperty("Text");
            return textProp?.GetValue(control)?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    protected override void OnDispose()
    {
        foreach (var kvp in _eventCache.Value)
        {
        }
    }
}
