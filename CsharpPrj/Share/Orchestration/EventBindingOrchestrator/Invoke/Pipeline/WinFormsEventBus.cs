
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using Modules.EventFramework.Abstractions;
using Modules.EventFramework.Adapters;
using Modules.EventFramework.Bus;
using Modules.EventFramework.Core;
using Modules.Pipeline.StaticPipeline;
using Share.EventBindingOrchestrator.Diagnostics;
using TConfig;

namespace Share.EventBindingOrchestrator.Adapters;

public class WinFormsEventBus : IEventBus
{
    private readonly DefaultEventBus _innerBus;
    private readonly IEventNameNormalizer _normalizer;
    private readonly ConcurrentDictionary<string, List<object>> _reflectionSubscriptions = new();

    private readonly StaticPipeline<EventBusContext> _bindPipeline;

    public WinFormsEventBus()
        : this(new DefaultEventNameNormalizer())
    {
    }

    public WinFormsEventBus(IEventNameNormalizer normalizer)
    {
        _innerBus = new DefaultEventBus();
        // 必填端口（T）：事件名规范化器由装配点显式提供；无参构造是组件自装配的唯一入口。
        _normalizer = PortGuard.Required(normalizer, nameof(normalizer));

        _bindPipeline = new StaticPipeline<EventBusContext>(new[]
        {
            new Step<EventBusContext>("EventNameNormalization", ctx => EventNameNormalizationStep(ctx, _normalizer)),
            new Step<EventBusContext>("EventInfoResolution", EventInfoResolutionStep),
            new Step<EventBusContext>("BindOrWrap", ctx => BindOrWrapStep(ctx, _reflectionSubscriptions)),
        });
    }

    public IEventSubscription Subscribe(string eventName, Func<EventContext, Task> handler)
        => _innerBus.Subscribe(eventName, handler);

    public Task PublishAsync(string eventName, EventContext context)
        => _innerBus.PublishAsync(eventName, context);

    public bool HasSubscribers(string eventName)
        => _innerBus.HasSubscribers(eventName);

    public void Clear()
        => _innerBus.Clear();

    public void BindControlEvent(object control, string eventName, Delegate handler)
    {
        if (control == null || string.IsNullOrEmpty(eventName) || handler == null)
            return;

        var ctx = new EventBusContext
        {
            Control = control,
            RawEventName = eventName,
            Handler = handler
        };

        try
        {
            _bindPipeline.Execute(ctx);
        }
        catch (Exception ex)
        {
            EventBindingDiagnosticContext.Record(EventNames.EventBinderTryBindEventReflectionFailed, new
            {
                controlName = ctx.ControlName,
                eventName = ctx.RawEventName,
                error = ex.Message
            });
            throw;
        }
    }


    public void Publish<TEvent>(TEvent @event) where TEvent : class
        => _innerBus.Publish(@event);

    public void Subscribe<TEvent>(object subscriber, Action<TEvent> handler) where TEvent : class
        => _innerBus.Subscribe(subscriber, handler);

    public void Unsubscribe<TEvent>(object subscriber) where TEvent : class
        => _innerBus.Unsubscribe<TEvent>(subscriber);

    public void Publish(string eventName, object? args)
        => _innerBus.Publish(eventName, args);

    public void Subscribe(string eventName, Action<object?> handler)
        => _innerBus.Subscribe(eventName, handler);

    public void Unsubscribe(string eventName, Action<object?> handler)
        => _innerBus.Unsubscribe(eventName, handler);

    public bool CanHandleControl(object control) => true;

    public void UnbindControlEvent(object control, string eventName)
    {
        if (control == null)
            return;

        var controlHash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(control).ToString();

        if (string.IsNullOrEmpty(eventName))
        {
            var keysToRemove = _reflectionSubscriptions.Keys
                .Where(k => k.StartsWith(controlHash + ".", StringComparison.Ordinal))
                .ToList();

            foreach (var removeKey in keysToRemove)
            {
                if (_reflectionSubscriptions.TryRemove(removeKey, out var removedHandlers))
                {
                    var eventNameFromKey = removeKey.Substring(controlHash.Length + 1);
                    try
                    {
                        var eventInfo = control.GetType().GetEvent(eventNameFromKey);
                        if (eventInfo != null)
                        {
                            foreach (var handler in removedHandlers)
                            {
                                eventInfo.RemoveEventHandler(control, handler as Delegate);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        EventBindingDiagnosticContext.Record(EventNames.EventBinderUnbindFailed, new
                        {
                            controlName = control.GetType().Name,
                            eventName = eventNameFromKey,
                            error = ex.Message
                        });
                    }
                }
            }

            return;
        }

        var normalizedEventName = _normalizer.ToPascalCase(eventName);
        var key = $"{controlHash}.{normalizedEventName}";
        if (_reflectionSubscriptions.TryRemove(key, out var handlers))
        {
            try
            {
                var eventInfo = control.GetType().GetEvent(normalizedEventName);
                if (eventInfo != null)
                {
                    foreach (var handler in handlers)
                    {
                        eventInfo.RemoveEventHandler(control, handler as Delegate);
                    }
                }
            }
            catch (Exception ex)
            {
                EventBindingDiagnosticContext.Record(EventNames.EventBinderUnbindFailed, new
                {
                    controlName = control.GetType().Name,
                    eventName = normalizedEventName,
                    error = ex.Message
                });
            }
        }

    }


    private static void EventNameNormalizationStep(EventBusContext ctx, IEventNameNormalizer normalizer)
    {
        ctx.NormalizedEventName = normalizer.ToPascalCase(ctx.RawEventName);
        ctx.ControlName = WinFormsEventSourceNode.GetControlName(ctx.Control);

        EventBindingDiagnosticContext.Record(EventNames.EventBinderTryBindEventEntry, new
        {
            controlName = ctx.ControlName,
            eventName = ctx.RawEventName,
            normalizedEventName = ctx.NormalizedEventName,
            handlerType = ctx.Handler.GetType().Name
        });
    }

    private static void EventInfoResolutionStep(EventBusContext ctx)
    {
        ctx.EventInfo = ctx.Control.GetType().GetEvent(ctx.NormalizedEventName);

        if (ctx.EventInfo == null)
        {
            var allEvents = string.Join(", ", ctx.Control.GetType().GetEvents().Select(e => e.Name));
            EventBindingDiagnosticContext.Record(EventNames.EventBinderTryBindEventEventNotFound, new
            {
                controlName = ctx.ControlName,
                normalizedEventName = ctx.NormalizedEventName,
                availableEvents = allEvents
            });

            ctx.BindingSuccess = false;
            ctx.BindingError = $"事件 '{ctx.NormalizedEventName}' 未找到";
            return;
        }

        ctx.EventHandlerType = ctx.EventInfo.EventHandlerType;
        ctx.IsDirectEventHandler = ctx.EventHandlerType == typeof(EventHandler);
    }

    private static void BindOrWrapStep(EventBusContext ctx, ConcurrentDictionary<string, List<object>> reflectionSubscriptions)
    {
        if (ctx.EventInfo == null)
            return;

        if (ctx.IsDirectEventHandler)
        {
            ctx.EventInfo.AddEventHandler(ctx.Control, ctx.Handler);
            ctx.BindingSuccess = true;
        }
        else
        {
            DynamicMethodBind(ctx);
        }

        if (ctx.BindingSuccess)
        {
            var key = $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ctx.Control)}.{ctx.NormalizedEventName}";
            reflectionSubscriptions.AddOrUpdate(
                key,
                _ => new List<object> { ctx.Handler },
                (_, list) => { list.Add(ctx.Handler); return list; });
        }
    }

    private static void DynamicMethodBind(EventBusContext ctx)
    {
        if (ctx.EventHandlerType == null)
        {
            ctx.BindingSuccess = false;
            ctx.BindingError = "EventHandlerType 为 null";
            return;
        }

        var invokeMethod = ctx.EventHandlerType.GetMethod("Invoke");
        if (invokeMethod != null)
        {
            var paramTypes = invokeMethod.GetParameters()
                .Select(p => p.ParameterType)
                .ToArray();

            var handlerInvoke = ctx.Handler.GetType().GetMethod("Invoke");
            if (handlerInvoke == null)
            {
                TryFallbackAddEventHandler(ctx);
                return;
            }

            var handlerParams = handlerInvoke.GetParameters();

            var dynamicMethod = new DynamicMethod(
                "WrapEventHandler",
                typeof(void),
                paramTypes,
                typeof(WinFormsEventBus).Module);
            var il = dynamicMethod.GetILGenerator();

            for (int i = 0; i < handlerParams.Length; i++)
            {
                switch (i)
                {
                    case 0: il.Emit(OpCodes.Ldarg_0); break;
                    case 1: il.Emit(OpCodes.Ldarg_1); break;
                    case 2: il.Emit(OpCodes.Ldarg_2); break;
                    case 3: il.Emit(OpCodes.Ldarg_3); break;
                    default:
                        il.Emit(OpCodes.Ldarg_S, (byte)i);
                        break;
                }
            }

            il.Emit(OpCodes.Callvirt, handlerInvoke);
            il.Emit(OpCodes.Ret);

            var wrappedDelegate = dynamicMethod.CreateDelegate(ctx.EventHandlerType, ctx.Handler.Target);
            ctx.EventInfo!.AddEventHandler(ctx.Control, wrappedDelegate);
            ctx.BindingSuccess = true;
        }
        else
        {
            TryFallbackAddEventHandler(ctx);
        }
    }

    private static void TryFallbackAddEventHandler(EventBusContext ctx)
    {
        try
        {
            ctx.EventInfo!.AddEventHandler(ctx.Control, ctx.Handler);
            ctx.BindingSuccess = true;
        }
        catch (Exception ex)
        {
            ctx.BindingSuccess = false;
            ctx.BindingError = ex.Message;

            EventBindingDiagnosticContext.Record(EventNames.EventBinderTryBindEventReflectionFailed, new
            {
                controlName = ctx.ControlName,
                eventName = ctx.NormalizedEventName,
                error = ex.Message
            });
        }
    }
}
