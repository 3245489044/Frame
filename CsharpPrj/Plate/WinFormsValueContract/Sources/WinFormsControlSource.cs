
using System.Reflection;
using ParameterResolver.Abstractions;
using ParameterResolver.Models;
using ParameterResolver.Strategies.Abstractions;

namespace WinFormsValueContract.Sources;

public class WinFormsControlSource : IParameterSource
{
    private readonly object _form;
    private readonly IReadStrategy<InputAdapter> _defaultReadStrategy;
    private readonly Func<object, InputAdapter?> _adapterResolver;

    public WinFormsControlSource(
        object form,
        IReadStrategy<InputAdapter> defaultReadStrategy,
        Func<object, InputAdapter?>? adapterResolver = null)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
        _defaultReadStrategy = defaultReadStrategy ?? throw new ArgumentNullException(nameof(defaultReadStrategy));
        _adapterResolver = adapterResolver ?? (_ => null);
    }

    public string? ReadValue(string controlName)
    {
        var control = FindControl(_form, controlName);
        if (control == null) return null;

        var adapter = _adapterResolver(control);
        var value = _defaultReadStrategy.Read(control, adapter);
        return value?.ToString();
    }

    public static object? FindControl(object container, string controlName)
    {
        if (container == null || string.IsNullOrEmpty(controlName))
            return null;

        var parts = controlName.Split('.');
        object? current = container;

        foreach (var part in parts)
        {
            if (current == null) return null;

            var type = current.GetType();

            var prop = type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null)
            {
                current = prop.GetValue(current);
                continue;
            }

            var field = type.GetField(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null)
            {
                current = field.GetValue(current);
                continue;
            }

            current = FindInControlsCollection(current, part);
            if (current != null)
                continue;

            return null;
        }

        return current;
    }

    private static object? FindInControlsCollection(object container, string controlName)
    {
        try
        {
            var controlsProp = container.GetType().GetProperty("Controls",
                BindingFlags.Public | BindingFlags.Instance);
            if (controlsProp == null) return null;

            var controls = controlsProp.GetValue(container);
            if (controls == null) return null;

            var controlsType = controls.GetType();

            var findMethod = controlsType.GetMethod("Find",
                new[] { typeof(string), typeof(bool) });
            if (findMethod != null)
            {
                var found = (Array?)findMethod.Invoke(controls, new object[] { controlName, true });
                if (found != null && found.Length > 0)
                    return found.GetValue(0);
            }

            var indexerProp = controlsType.GetProperty("Item",
                new[] { typeof(string) });
            if (indexerProp != null)
            {
                try
                {
                    return indexerProp.GetValue(controls, new object[] { controlName });
                }
                catch
                {
                    return null;
                }
            }
        }
        catch
        {
        }

        return null;
    }
}
