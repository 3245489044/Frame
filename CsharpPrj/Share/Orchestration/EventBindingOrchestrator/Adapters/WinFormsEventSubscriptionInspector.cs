
#nullable enable

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Share.EventBindingOrchestrator.Abstractions;

namespace Share.EventBindingOrchestrator.Adapters;

public sealed class WinFormsEventSubscriptionInspector : IEventSubscriptionInspector
{
    private readonly ConcurrentDictionary<(Type, string), FieldInfo?> _fieldCache = new();

    private static readonly PropertyInfo s_eventsProperty = typeof(Control).GetProperty(
        "Events",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Control.Events 属性未找到（WinForms 运行时异常）。");

    public bool? HasSubscribers(Control control, string eventName)
    {
        if (control == null || string.IsNullOrWhiteSpace(eventName))
            return false;

        var pascal = Normalize(eventName);
        var key = (control.GetType(), pascal);
        var field = _fieldCache.GetOrAdd(key, _ => ProbeEventField(pascal));

        if (field == null)
            return null;

        var events = s_eventsProperty.GetValue(control) as EventHandlerList;
        var keyObj = field.GetValue(null);
        return events?[keyObj] is Delegate d && d.GetInvocationList().Length > 0;
    }

    private static FieldInfo? ProbeEventField(string pascalEvent)
    {
        return typeof(Control)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(f =>
                f.FieldType == typeof(object) &&
                f.IsStatic &&
                f.Name.EndsWith("Event", StringComparison.OrdinalIgnoreCase) &&
                f.Name.Contains(pascalEvent, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string eventName)
        => eventName.Length == 0
            ? eventName
            : char.ToUpperInvariant(eventName[0]) + eventName.Substring(1);
}