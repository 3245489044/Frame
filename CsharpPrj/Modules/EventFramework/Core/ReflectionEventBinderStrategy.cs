
using System.Reflection;

namespace Modules.EventFramework.Core;

public static class ReflectionEventBinder
{
    public static bool BindEvent(object target, string eventName, Delegate handler)

    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名不能为空", nameof(eventName));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        try
        {
            var eventInfo = target.GetType().GetEvent(
                eventName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (eventInfo == null)
                return false;

            eventInfo.AddEventHandler(target, handler);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool UnbindEvent(object target, string eventName, Delegate handler)

    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名不能为空", nameof(eventName));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        try
        {
            var eventInfo = target.GetType().GetEvent(
                eventName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (eventInfo == null)
                return false;

            eventInfo.RemoveEventHandler(target, handler);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool UnbindAll(object target, string eventName)

    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名不能为空", nameof(eventName));

        try
        {
            var eventInfo = target.GetType().GetEvent(
                eventName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (eventInfo == null)
                return false;

            var fieldName = eventName;
            var field = target.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            if (field == null)
            {
                field = target.GetType().GetField($"Event{fieldName}",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            }

            if (field != null)
            {
                field.SetValue(target, null);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool BindEventFromConfig(object target, object eventConfig, Delegate handler)

    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (eventConfig == null)
            throw new ArgumentNullException(nameof(eventConfig));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        var eventNameProp = eventConfig.GetType().GetProperty("EventName");
        var eventName = eventNameProp?.GetValue(eventConfig)?.ToString();

        if (!string.IsNullOrEmpty(eventName))
            return BindEvent(target, eventName, handler);

        return false;
    }

    public static bool CanHandle(object target)

    {
        return target != null;
    }
}
