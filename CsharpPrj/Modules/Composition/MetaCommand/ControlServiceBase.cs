using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Modules.Threading;

namespace MetaCommand
{
    public abstract class ControlServiceBase : IControlService
    {
        /// <summary>
        /// 值语义契约服务（P4 单轨化）：控件值读写委托契约层（TConfig.Controls.IControlService →
        /// ValueContractRegistry / ControlValueBridge）。MetaCommand 不再维护自己的控件类型适配器表
        /// （"每种 UI 单独适配"残留消除）；本基类仅保留查找/事件绑定/UI 调度等宿主能力。
        /// </summary>
        protected abstract TConfig.Controls.IControlService ValueService { get; }

        public static bool IsProgrammaticWrite(object control) => ReentryGuard.IsReentering(control);


        public object? GetValue(object control)
        {
            ArgumentNullException.ThrowIfNull(control);
            return ValueService.GetValue(control)
                ?? throw new NotSupportedException($"不支持的控件类型: {control.GetType().FullName}");
        }

        public void SetValue(object control, object? value)
        {
            ArgumentNullException.ThrowIfNull(control);

            using (ReentryGuard.Enter(control))
            {
                ValueService.SetValue(control, value);
            }
        }

        public bool IsSupported(object control)
            => control != null && ValueService.IsSupported(control);

        public void RegisterAdapter(Type controlType, Func<object, object?> getter, Action<object, object?> setter)
            => throw new NotSupportedException(
                "MetaCommand 不再维护控件值适配器表（P4 单轨化）：值语义统一经 ValueContractRegistry.Register(ControlValueContract) 注册");

        public void ClearAdapters()
        {
            // 契约注册表为全局单点，不由 MetaCommand 清空（避免影响 ECS 链路同享的契约）
        }

        // P4 单轨化：原 _valueAdapters/FindAdapter（类型→(getter,setter) 注册表）已删除——
        // 值语义统一经 ValueService（ValueContractRegistry，含基类回退）解析。



        private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> _propertyCache = new();

        protected static IEnumerable? GetControlsCollection(object control)
        {
            var prop = _propertyCache.GetOrAdd(
                (control.GetType(), "Controls"),
                key => key.Type.GetProperty(key.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy));
            return prop?.GetValue(control) as IEnumerable;
        }

        protected static T? GetPropertyValue<T>(object obj, string propertyName)
        {
            try
            {
                var prop = _propertyCache.GetOrAdd(
                    (obj.GetType(), propertyName),
                    key => key.Type.GetProperty(key.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy));
                var value = prop?.GetValue(obj);
                return value is T tValue ? tValue : default;
            }
            catch { return default; }
        }

        public virtual object? FindByName(object root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;

            var controls = GetControlsCollection(root);
            if (controls == null)
                return null;

            foreach (var child in controls)
            {
                var childName = GetPropertyValue<string>(child, "Name");
                if (string.Equals(childName, name, StringComparison.OrdinalIgnoreCase))
                    return child;

                var found = FindByName(child, name);
                if (found != null)
                    return found;
            }

            return null;
        }

        public virtual object? FindByName(string name) => null;

        public object? FindByType(object root, Type type)
        {
            if (root == null || type == null)
                return null;
            if (type.IsInstanceOfType(root))
                return root;

            var controls = GetControlsCollection(root);
            if (controls == null)
                return null;

            foreach (var child in controls)
            {
                if (type.IsInstanceOfType(child))
                    return child;
                var found = FindByType(child, type);
                if (found != null)
                    return found;
            }

            return null;
        }

        public List<object> FindAll(object root, Func<object, bool> predicate)
        {
            var results = new List<object>();
            FindAllRecursive(root, predicate, results);
            return results;
        }

        private void FindAllRecursive(object root, Func<object, bool> predicate, List<object> results)
        {
            if (root == null || predicate == null)
                return;

            if (predicate(root))
                results.Add(root);

            var controls = GetControlsCollection(root);
            if (controls != null)
            {
                foreach (var child in controls)
                    FindAllRecursive(child, predicate, results);
            }
        }

        public object? FindForm(object control)
        {
            var current = control;
            while (current != null)
            {
                var parent = GetPropertyValue<object>(current, "Parent");
                if (parent == null)
                    return current;
                current = parent;
            }
            return null;
        }


        private readonly ConcurrentDictionary<string, EventInfo?> _eventInfoCache = new();
        private readonly Dictionary<string, string> _eventNameMap = new(StringComparer.OrdinalIgnoreCase);

        protected void RegisterEventMapping(string alias, string actualEventName)
            => _eventNameMap[alias] = actualEventName;

        private readonly ConcurrentDictionary<string, List<(Delegate Handler, EventInfo EventInfo)>> _boundEvents = new();

        public void BindEvent(object control, string eventName, Delegate handler)
        {
            ArgumentNullException.ThrowIfNull(control);
            ArgumentNullException.ThrowIfNull(handler);
            if (string.IsNullOrWhiteSpace(eventName))
                throw new ArgumentNullException(nameof(eventName));

            var eventInfo = GetEventInfo(control.GetType(), eventName)
                ?? throw new ArgumentException($"控件类型 {control.GetType().Name} 上找不到事件 '{eventName}'", nameof(eventName));

            eventInfo.AddEventHandler(control, handler);

            var controlKey = GetControlEventKey(control, eventName);
            _boundEvents.AddOrUpdate(controlKey,
                _ => new List<(Delegate, EventInfo)> { (handler, eventInfo) },
                (_, list) => { list.Add((handler, eventInfo)); return list; });
        }

        public void UnbindEvent(object control, string eventName)
        {
            ArgumentNullException.ThrowIfNull(control);
            if (string.IsNullOrWhiteSpace(eventName))
                return;

            var controlKey = GetControlEventKey(control, eventName);
            if (_boundEvents.TryRemove(controlKey, out var handlers))
            {
                foreach (var (handler, eventInfo) in handlers)
                {
                    try
                    {
                        eventInfo.RemoveEventHandler(control, handler);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public void UnbindAllEvents(object control)
        {
            ArgumentNullException.ThrowIfNull(control);

            var keysToRemove = _boundEvents.Keys
                .Where(k => k.StartsWith(GetControlPrefix(control), StringComparison.Ordinal))
                .ToList();

            foreach (var key in keysToRemove)
            {
                if (_boundEvents.TryRemove(key, out var handlers))
                {
                    foreach (var (handler, eventInfo) in handlers)
                    {
                        try
                        {
                            eventInfo.RemoveEventHandler(control, handler);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private static string GetControlEventKey(object control, string eventName)
            => $"{GetControlPrefix(control)}.{eventName}";

        private static string GetControlPrefix(object control)
            => $"{control.GetType().FullName}@{control.GetHashCode()}";


        public string[] GetSupportedEvents(Type controlType)
            => controlType.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                .Select(e => e.Name).Distinct().ToArray();

        private EventInfo? GetEventInfo(Type controlType, string eventName)
        {
            var cacheKey = $"{controlType.FullName}.{eventName}";
            return _eventInfoCache.GetOrAdd(cacheKey, _ =>
            {
                if (_eventNameMap.TryGetValue(eventName, out var mappedName))
                    eventName = mappedName;

                var type = controlType;
                while (type != null && type != typeof(object))
                {
                    var eventInfo = type.GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (eventInfo != null)
                        return eventInfo;
                    type = type.BaseType;
                }
                return null;
            });
        }


        public abstract void InvokeIfRequired(object control, Action action);
        public abstract Task RunOnUiThread(object control, Func<Task> asyncAction);
        public abstract void ExecuteOnUiThread(object control, Action action);


        public virtual void RegisterControl(string name, object control) { }

        public virtual void UnregisterControl(string name) { }

        public virtual object? FindRegistered(string name) => null;

        public virtual void ClearRegistry() { }


        public virtual bool TrySetControlValue(string controlName, object? value, object? container = null)
        {
            var control = container != null
                ? FindByName(container, controlName)
                : FindByName(controlName);
            if (control == null) return false;
            SetValue(control, value);
            return true;
        }

        public virtual object? TryGetControlValue(string controlName, object? container = null)
        {
            var control = container != null
                ? FindByName(container, controlName)
                : FindByName(controlName);
            return control != null ? GetValue(control) : null;
        }
    }
}
