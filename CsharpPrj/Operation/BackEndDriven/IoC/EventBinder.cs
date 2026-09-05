
using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Modules.EventFramework.Abstractions;
using Modules.Pipeline;
using Share.EventBindingOrchestrator;
using Share.EventBindingOrchestrator.Abstractions;
using Share.EventBindingOrchestrator.Diagnostics;
using Share.EventBindingOrchestrator.Parameters;
using Share.EventBindingOrchestrator.Utils;
using TConfig;

namespace BackEndDriven.IoC;

public class EventBinder : IEventBinder
{
    private readonly ILogger<EventBinder>? _logger;

    public EventBinder(ILogger<EventBinder>? logger = null)
    {
        _logger = logger;
    }

    public bool TryBindEvent(
        object eventBus,
        object control,
        string normalizedEventName,
        Delegate handler,
        object errors)
    {
        var controlName = GetControlName(control);

        if (eventBus is IEventBus typedEventBus)
        {
            try
            {
                typedEventBus.BindControlEvent(control, normalizedEventName, handler);
                EventBindingDiagnosticContext.Record("eventbinder.try_bind_event.success_via_eventbus", new
                {
                    controlName,
                    normalizedEvent = normalizedEventName,
                    handlerType = handler.GetType().Name
                });
                return true;
            }
            catch (Exception ex)
            {
                EventBindingDiagnosticContext.Record("eventbinder.try_bind_event.reflection_fallback", new
                {
                    controlName,
                    normalizedEvent = normalizedEventName,
                    error = ex.Message
                });

                if (errors is ErrorCollector errorCollector)
                {
                    errorCollector.Add(
                        "EVENT_BIND_EVENTBUS_FAILED",
                        $"EventBus 绑定事件失败: control={controlName}, event={normalizedEventName}, error={ex.Message}",
                        ErrorLevel.Warning,
                        ex);
                }
            }
        }

        if (BindEventFallback(control, normalizedEventName, handler))
        {
            EventBindingDiagnosticContext.Record("eventbinder.try_bind_event.success_via_reflection", new
            {
                controlName,
                normalizedEvent = normalizedEventName
            });
            return true;
        }

        var allEvents = string.Join(", ", control.GetType().GetEvents().Select(e => e.Name));
        EventBindingDiagnosticContext.Record("eventbinder.try_bind_event.event_not_found", new
        {
            controlName,
            normalizedEvent = normalizedEventName,
            availableEvents = allEvents
        });

        if (errors is ErrorCollector errorCollector2)
        {
            errorCollector2.Add(
                "EVENT_BIND_FAILED",
                $"事件绑定失败: control={controlName}, event={normalizedEventName}, availableEvents=[{allEvents}]",
                ErrorLevel.Warning);
        }

        return false;
    }

    public void RecordBoundControl(
        object input,
        object control,
        int boundEventCount,
        string diagnosticKey,
        object? eventsConfig = null,
        string? eventName = null,
        string? functionName = null)
    {
        if (boundEventCount <= 0)
            return;

        var controlName = GetControlName(control);

        if (input is EventBindingContext bindingCtx)
        {
            bindingCtx.AddBoundControl(new ControlBindingInfo
            {
                ControlName = controlName,
                ControlType = control?.GetType().Name ?? "unknown",
                Status = "Bound",
                BoundEventCount = boundEventCount
            });
        }

        object diagnosticData;
        if (eventsConfig is EventConfig typedEventConfig)
        {
            diagnosticData = new
            {
                controlName,
                eventCount = boundEventCount,
                events = typedEventConfig.EventsDictionary != null
                    ? string.Join(",", typedEventConfig.EventsDictionary.Keys)
                    : ""
            };
        }
        else
        {
            diagnosticData = new
            {
                controlName,
                eventName,
                functionName,
                eventCount = boundEventCount
            };
        }

        EventBindingDiagnosticContext.Record(diagnosticKey, diagnosticData);
    }

    public async Task HandleExecutionResult(ExecutionResult result)
    {
        var functionName = result.Metadata?.TryGetValue("FunctionName", out var fn) == true ? fn?.ToString() : null;
        var outputKey = result.Metadata?.TryGetValue("OutputKey", out var ok) == true ? ok?.ToString() : null;
        var eventName = result.Metadata?.TryGetValue("EventName", out var en) == true ? en?.ToString() : null;
        var outputTarget = result.Metadata?.TryGetValue("OutputTarget", out var ot) == true ? ot?.ToString() : null;
        var sender = result.Metadata?.TryGetValue("Sender", out var s) == true ? s : null;
        var onResultObj = result.Metadata?.TryGetValue("OnResult", out var or) == true ? or : null;
        var onResult = onResultObj as Action<string, string, object?>;

        var isSuccess = result.IsSuccess;
        var value = result.Value;
        var errorMessage = result.ErrorMessage;
        var exception = result.Exception;
        var executionTime = result.ExecutionTime;
        var status = result.Status.ToString();

        if (result.IsAccepted)
        {
            EventBindingDiagnosticContext.Record("eventbinder.function.accepted", new
            {
                name = functionName,
                eventName,
                note = "请求已入队待执行（中间状态，非失败），最终结果由 ECS 管线异步回调"
            });

            onResult?.Invoke(functionName, "accepted", new
            {
                eventName,
                note = "请求已入队待执行"
            });
            await Task.CompletedTask;
            return;
        }

        if (isSuccess)
        {
            object? resolvedValue = value;

            if (resolvedValue is IDictionary dict &&
                !string.IsNullOrEmpty(outputKey) &&
                dict.Contains(outputKey))
            {
                resolvedValue = dict[outputKey];
            }

            EventBindingDiagnosticContext.Record("eventbinder.function.success", new
            {
                name = functionName,
                result = resolvedValue?.ToString(),
                resultType = resolvedValue?.GetType().Name,
                executionTimeMs = executionTime.TotalMilliseconds
            });

            if (outputTarget != null && resolvedValue != null)
            {
                WriteOutputToControl(sender, outputTarget, resolvedValue);
            }
            else
            {
                EventBindingDiagnosticContext.Record("eventbinder.function.skip_write", new
                {
                    name = functionName,
                    outputTarget,
                    resolvedValueIsNull = resolvedValue == null,
                    resolvedValueType = resolvedValue?.GetType().Name
                });
            }

            onResult?.Invoke(functionName, "success", new
            {
                result = resolvedValue,
                resultType = resolvedValue?.GetType().Name,
                eventName,
                executionTime = executionTime.TotalMilliseconds,
                status
            });
        }
        else
        {
            EventBindingDiagnosticContext.Record("eventbinder.function.exception", new
            {
                name = functionName,
                error = errorMessage ?? "Unknown error",
                hasException = exception != null,
                exceptionType = exception?.GetType().Name,
                exceptionMessage = exception?.Message,
                exceptionStackTrace = exception?.StackTrace
            });

            onResult?.Invoke(functionName, "failed", new
            {
                error = errorMessage ?? "Unknown error",
                eventName
            });

            if (outputTarget != null)
            {
                var errorMsg = $"[ERROR] {functionName} 执行失败: {errorMessage ?? "Unknown error"}";
                WriteOutputToControl(sender, outputTarget, errorMsg);
            }
        }

        await Task.CompletedTask;
    }

    public void WriteOutputToControl(object sender, string outputTarget, object value)
    {
        if (sender == null || string.IsNullOrEmpty(outputTarget) || value == null)
        {
            EventBindingDiagnosticContext.Record("eventbinder.write_output.skipped_null_param", new
            {
                senderIsNull = sender == null,
                senderType = sender?.GetType().Name,
                outputTarget,
                valueIsNull = value == null
            });
            return;
        }

        try
        {
            var control = sender as System.Windows.Forms.Control;
            if (control == null)
            {
                BackEndDriven.Diagnostics.IocLog.Log("IoC_WriteOutput_SenderNotControl", new { senderType = sender.GetType().Name });
                return;
            }

            var searchRoot = control.FindForm();
            if (searchRoot == null)
            {
                BackEndDriven.Diagnostics.IocLog.Log("IoC_WriteOutput_NoFormFound", new { controlName = control.Name, outputTarget });
                return;
            }

            var targetControl = ControlTreeWalker.FindControl(searchRoot, outputTarget, null)
                as System.Windows.Forms.Control;
            if (targetControl == null)
            {
                BackEndDriven.Diagnostics.IocLog.Log("IoC_WriteOutput_TargetNotFound", new { outputTarget, searchRoot = searchRoot.Name });
                return;
            }

            var textProp = targetControl.GetType().GetProperty("Text");
            if (textProp != null && textProp.CanWrite)
            {
                textProp.SetValue(targetControl, value?.ToString() ?? string.Empty);
                BackEndDriven.Diagnostics.IocLog.Log("IoC_WriteOutput_Written", new { value, outputTarget });
            }
            else
            {
                BackEndDriven.Diagnostics.IocLog.Log("IoC_WriteOutput_NoTextProperty", new { outputTarget });
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"IoC WriteOutput 失败: outputTarget={outputTarget}, valueType={(value?.GetType().Name ?? "null")}", ex);
        }
    }


    private static string? GetControlName(object control)
        => EventBindingHelper.GetControlName(control);

    private static bool BindEventFallback(object control, string eventName, Delegate handler)
    {
        try
        {
            var eventInfo = control.GetType().GetEvent(eventName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (eventInfo != null)
            {
                eventInfo.AddEventHandler(control, handler);
                return true;
            }
        }
        catch
        {
        }
        return false;
    }
}
