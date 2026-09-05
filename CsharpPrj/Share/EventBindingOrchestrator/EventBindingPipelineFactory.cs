
using System.Linq;
using System.Runtime.ExceptionServices;
using FluentValidation;
using FrameworkAbstractions.ComponentRegistry;
using Microsoft.Extensions.Logging;
using Modules.EventFramework.Abstractions;
using Modules.EventFramework.Core;
using Modules.EventFramework.Utils;
using TConfig;

using Modules.Pipeline;
using Modules.Pipeline.StaticPipeline;
using Modules.Transformers;
using Framework.Tree;
using Share.EventBindingOrchestrator.Diagnostics;
using Share.EventBindingOrchestrator.Parameters;
using Share.EventBindingOrchestrator.Utils;

namespace Share.EventBindingOrchestrator.Transformers;

public static class EventBindingPipelineFactory
{


    public static StaticPipeline<EventBindingContext> CreateFullUnifiedPipeline(
        ILogger? logger = null,
        ErrorCollector? globalStats = null,
        IFunctionResolver? functionResolver = null,
        IParameterBinder? parameterBinder = null,
        IEventBinder? eventBinder = null,
        Action<string, string, object?>? onEventResult = null,
        object? defaultTargetObject = null,
        UIConfiguration? defaultConfig = null,
        EventBindingOptions? defaultOptions = null)
    {
        var executeFunctionDelegate = defaultOptions?.ExecuteFunctionDelegate;

        return new StaticPipeline<EventBindingContext>(
            new[]
            {
                new Step<EventBindingContext>("ContextInit", ctx => ContextInitStep(ctx, defaultTargetObject, defaultConfig, defaultOptions), IsFatal: false),
                new Step<EventBindingContext>("ValidateInput", ValidateInputStep, IsFatal: true),
                new Step<EventBindingContext>("ControlTreeWalk", ctx => ControlTreeWalkStep(ctx, functionResolver, parameterBinder, eventBinder, onEventResult, globalStats, executeFunctionDelegate), IsFatal: false),
                new Step<EventBindingContext>("DiagnosticsScope", DiagnosticsScopeStep, IsFatal: false),
                new Step<EventBindingContext>("EmptyViewGuard", ctx => EmptyViewGuardStep(ctx, logger), IsFatal: false),
                new Step<EventBindingContext>("StatisticsCollect", ctx => StatisticsCollectStep(ctx, globalStats), IsFatal: false),
            },
            onStepError: (ctx, stepName, ex) =>
            {
                ctx.ErrorCollector.Add($"PIPELINE_STEP_{stepName}",
                    ex.Message, ErrorLevel.Error, ex);

                var controlName = ctx.CurrentDeclaration?.SourceName
                    ?? ctx.CurrentControlConfig?.Name
                    ?? "(unknown)";
                ctx.AddSkippedControl(new TConfig.ControlBindingInfo
                {
                    ControlName = controlName,
                    Status = "Error",
                    ErrorMessage = $"[{stepName}] {ex.Message}"
                });
            });
    }


    private static void ContextInitStep(EventBindingContext ctx, object? defaultTargetObject, UIConfiguration? defaultConfig, EventBindingOptions? defaultOptions)
    {
        ctx.TargetObject ??= defaultTargetObject;
        ctx.Config ??= defaultConfig;

        if (defaultOptions != null)
        {
            ctx.SearchRoot ??= defaultOptions.SearchRoot;
            ctx.EventBus ??= defaultOptions.EventBus;
            ctx.IsScriptExecutionMode = defaultOptions.ScriptExecutionEnabled;
        }
    }


    private static void ControlTreeWalkStep(EventBindingContext ctx,
        IFunctionResolver? functionResolver, IParameterBinder? parameterBinder,
        IEventBinder? eventBinder, Action<string, string, object?>? onEventResult,
        ErrorCollector errors,
        Func<string, Dictionary<string, object>, Task>? executeFunctionDelegate = null)
    {
        if (ctx.CurrentDeclaration != null || ctx.CurrentControlConfig != null)
        {
            ControlFinderStep(ctx);
            BindingMetadataStep(ctx);
            EventBinderStep(ctx, functionResolver, parameterBinder, eventBinder, onEventResult, errors, executeFunctionDelegate);
            return;
        }

        if (ctx.Config?.Views == null || ctx.Config.Views.Count == 0)
            return;

        ControlTreeWalker.WalkAllViews(ctx.Config, ctrl =>
        {
            if (ctrl?.Events?.EventsDictionary == null || ctrl.Events.EventsDictionary.Count == 0)
                return;

            ctx.CurrentControlConfig = ctrl;
            ControlFinderStep(ctx);
            BindingMetadataStep(ctx);
            EventBinderStep(ctx, functionResolver, parameterBinder, eventBinder, onEventResult, errors, executeFunctionDelegate);
        });

    }


    private static readonly Validation.EventBindingContextValidator s_validator = new();

    private static void ValidateInputStep(EventBindingContext ctx)
    {
        var result = s_validator.Validate(ctx);
        if (!result.IsValid)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.ErrorMessage));
            throw new ValidationException($"ValidateInput 失败: {errors}");
        }
    }

    private static void DiagnosticsScopeStep(EventBindingContext ctx)
    {
        EventBindingDiagnosticContext.Record("pipeline.orchestrator.start", new
        {
            targetType = ctx.TargetObject?.GetType().Name,
            viewCount = ctx.Config?.Views?.Count ?? 0,
            hasSearchRoot = ctx.SearchRoot != null,
            searchRootType = ctx.SearchRoot?.GetType().Name
        });
    }

    private static void EmptyViewGuardStep(EventBindingContext ctx, ILogger? logger)
    {
        if (ctx.CurrentDeclaration != null)
            return;

        if (ctx.Config?.Views == null || ctx.Config.Views.Count == 0)
        {
            var targetName = ctx.TargetObject?.GetType().Name ?? "(unknown)";
            logger?.LogWarning("UIConfiguration.Views is null or empty, target={Target}", targetName);
            ctx.SkipPipeline = true;
        }
    }

    private static void StatisticsCollectStep(EventBindingContext ctx, ErrorCollector? globalStats)
    {
        if (globalStats == null || ctx.SkipPipeline)
            return;

        globalStats.Add("STATS_BOUND_COUNT", ctx.BoundCount.ToString(), ErrorLevel.Info);
        globalStats.Add("STATS_SKIPPED_COUNT", ctx.SkippedCount.ToString(), ErrorLevel.Info);
    }


    private static void ControlFinderStep(EventBindingContext ctx)
    {
        CheckScriptModeGuard(ctx);

        if (ctx.SkipCurrentControl)
            return;

        var controlName = ctx.CurrentControlConfig?.Name
            ?? ctx.CurrentDeclaration?.SourceName;
        if (string.IsNullOrWhiteSpace(controlName))
        {
            ctx.CurrentControl = null;
            return;
        }

        ValidateControlInConfig(ctx, controlName);

        var scopePath = ResolveScopePath(ctx.Config, controlName);
        ctx.CurrentScopePath = scopePath;

        var searchRoot = ctx.SearchRoot ?? ctx.TargetObject;
        ctx.CurrentControl = ControlTreeWalker.FindControl(searchRoot, controlName, scopePath);
    }

    private static string? ResolveScopePath(UIConfiguration? config, string controlName)
    {
        if (config == null || string.IsNullOrWhiteSpace(controlName))
            return null;

        var pathMap = ControlTreeWalker.BuildControlPathMap(config);
        if (pathMap.TryGetValue(controlName, out var path))
            return path;

        return null;
    }

    private static void CheckScriptModeGuard(EventBindingContext input)
    {
        if (!input.IsScriptExecutionMode)
            return;

        var config = input.CurrentControlConfig;
        var declaration = input.CurrentDeclaration;

        bool hasScript = false;

        if (declaration != null)
        {
            hasScript = declaration.FunctionName?.StartsWith("script:", StringComparison.OrdinalIgnoreCase) == true;
        }

        if (!hasScript && config != null)
        {
            var script = EventBindingHelper.GetExtensionDataValue(config, "script");
            hasScript = !string.IsNullOrWhiteSpace(script);
        }

        if (!hasScript)
        {
            var controlName = declaration?.SourceName
                ?? config?.Name
                ?? "(unknown)";
            input.AddSkippedControl(new TConfig.ControlBindingInfo
            {
                ControlName = controlName,
                Status = "Skipped",
                SkipReason = "script_mode_guard: 脚本执行模式下函数名必须以 script: 前缀，当前不满足"
            });
            input.SkipCurrentControl = true;
        }
    }

    private static void ValidateControlInConfig(EventBindingContext context, string? controlName)
    {
        if (context.Config?.Views == null) return;

        if (string.IsNullOrWhiteSpace(controlName)) return;

        foreach (var view in context.Config.Views)
        {
            var found = ControlTreeWalker.FindControl(view.Controls, controlName);
            if (found != null) return;
        }

        EventBindingDiagnosticContext.Record(EventNames.ControlFinderConfigValidationWarning, new
        {
            controlName,
            message = "控件不在 UIConfiguration 配置树中，可能是动态创建的控件，继续尝试反射查找"
        });
    }

    private static void BindingMetadataStep(EventBindingContext input)
    {
        if (input.SkipCurrentControl)
            return;

        if (input.CurrentDeclaration != null)
        {
            ExtractFromDeclaration(input, input.CurrentDeclaration);
            return;
        }

        var config = input.CurrentControlConfig;
        if (config == null)
            return;

        ExtractFromConfig(input, config);
    }

    private static void ExtractFromDeclaration(EventBindingContext input, EventBindingDeclaration<IReadOnlyList<ParameterBinding>> declaration)
    {
        // D6 收敛：声明已持有配置层参数 DTO（TConfig.ParameterBinding），直接透传，废除 ParameterMapping→ParameterBinding 回转换
        if (declaration.Parameters is { Count: > 0 })
        {
            input.CurrentParameterBindings = declaration.Parameters;
        }

        if (!string.IsNullOrWhiteSpace(declaration.OutputTarget))
        {
            input.CurrentOutput = declaration.OutputTarget;
        }
    }

    private static void ExtractFromConfig(EventBindingContext input, ControlConfig config)
    {
        if (config.Events?.Bindings?.Parameters is { Count: > 0 })
        {
            input.CurrentParameterBindings = config.Events.Bindings.Parameters;
        }
    }

    private static void EventBinderStep(EventBindingContext ctx, IFunctionResolver? functionResolver, IParameterBinder? parameterBinder, IEventBinder? eventBinder, Action<string, string, object?>? onEventResult, ErrorCollector errors,
        Func<string, Dictionary<string, object>, Task>? executeFunctionDelegate = null)
    {
        if (ctx.SkipCurrentControl)
            return;

        if (executeFunctionDelegate == null)
        {
            if (ctx.EventBus is IEventBus eventBus && eventBus.GetType().GetProperty("Options")?.GetValue(eventBus) is EventBindingOptions options)
            {
                executeFunctionDelegate = options.ExecuteFunctionDelegate;
            }
        }

        if (ctx.CurrentControlConfig != null)
            ConfigBindingStep(ctx, functionResolver, parameterBinder, eventBinder, onEventResult, errors, executeFunctionDelegate);
        else if (ctx.CurrentDeclaration != null)
            DeclarationBindingStep(ctx, functionResolver, parameterBinder, eventBinder, onEventResult, errors, executeFunctionDelegate);
    }


    private static void ConfigBindingStep(EventBindingContext ctx, IFunctionResolver? functionResolver, IParameterBinder? parameterBinder, IEventBinder? eventBinder, Action<string, string, object?>? onEventResult, ErrorCollector errors,
        Func<string, Dictionary<string, object>, Task>? executeFunctionDelegate = null)
    {
        var config = ctx.CurrentControlConfig;
        var control = ctx.CurrentControl;

        if (config == null)
        {
            var controlName = control != null
                ? EventBindingHelper.GetControlName(control)
                : "(null)";
            ctx.AddSkippedControl(new TConfig.ControlBindingInfo
            {
                ControlName = controlName,
                Status = "Skipped",
                SkipReason = "config 为 null"
            });
            return;
        }

        var resolvedControlName = control != null
            ? EventBindingHelper.GetControlName(control)
            : "(null)";
        var parameterBindings = ctx.CurrentParameterBindings;

        if (config.Events != null)
        {
            if (ctx.EventBus == null)
                throw new InvalidOperationException(
                    $"EventBus 未设置，无法绑定控件 '{resolvedControlName}' 的事件。");

            if (control == null)
            {
                throw new InvalidOperationException(
                    $"ConfigBindingStep: 控件 '{resolvedControlName}' 未找到，无法绑定事件。"
                    + "请检查 ControlFinderStrategy 是否能正确查找该控件。"
                    + $"CurrentControlConfig.Name='{config.Name}', SearchRoot 可能未正确设置。");
            }

            var eventsConfig = config.Events;
            var boundEventCount = 0;

            if (eventsConfig.EventsDictionary == null || eventsConfig.EventsDictionary.Count == 0)
            {
                throw new InvalidOperationException(
                    $"ConfigBindingStep: 控件 '{resolvedControlName}' 的 EventsDictionary 为空。"
                    + "请检查 JSON 配置中 events 格式是否正确。"
                    + $"config.Events 不为 null 但 EventsDictionary 为空。");
            }

            foreach (var kv in eventsConfig.EventsDictionary)
            {
                var eventName = kv.Key;
                var functionNames = kv.Value;
                if (functionNames == null || functionNames.Count == 0)
                    continue;

                foreach (var functionName in functionNames)
                {
                    if (string.IsNullOrWhiteSpace(functionName))
                        continue;

                    var (resolvedOutputTarget, resolvedOutputKey) = EventBindingHelper.ResolveOutput(eventsConfig.Bindings, functionName);

                    if (BindSingleEvent(ctx, control, resolvedControlName, eventName, functionName,
                            resolvedOutputTarget, resolvedOutputKey,
                            functionResolver, parameterBinder, eventBinder, onEventResult, errors, executeFunctionDelegate))
                    {
                        boundEventCount++;
                    }
                }
            }

            eventBinder?.RecordBoundControl(ctx, control, boundEventCount,
                EventNames.EventBinderBoundViaEventBus, eventsConfig: eventsConfig);
        }
    }


    private static Delegate CreateEventHandler(
        object control,
        string normalizedEventName,
        string functionName,
        IReadOnlyList<ParameterBinding>? parameterBindings,
        string? outputTarget,
        string? outputKey,
        Action<string, string, object?>? onResult,
        IParameterBinder? parameterBinder,
        IEventBinder? eventBinder,
        Func<string, Dictionary<string, object>, Task>? executeFunctionDelegate = null)
    {
        EventHandler handler = async (sender, e) =>
        {
            try
            {
                // D6 收敛：触发期直接从绑定描述构建参数（废除预计算 ParameterSource 层）
                var parameters = parameterBinder?.Build(
                    parameterBindings,
                    (sender as System.Windows.Forms.Control)?.FindForm(),
                    sender) ?? new Dictionary<string, object>();

                ExecutionResult result;
                try
                {
                    if (executeFunctionDelegate != null)
                    {
                        await executeFunctionDelegate(functionName, parameters);
                    }
                    result = ExecutionResult.Accepted(functionName, parameters);
                }
                catch (Exception ex)
                {
                    result = ExecutionResult.Failure(ex.Message, ex);
                }

                PopulateMetadata(result, functionName, outputKey, normalizedEventName, outputTarget, sender, onResult);

                if (eventBinder != null)
                {
                    await eventBinder.HandleExecutionResult(result);
                }
            }
            catch (Exception ex)
            {
                if (eventBinder != null)
                {
                    var failResult = ExecutionResult.Failure(ex.Message, ex);
                    PopulateMetadata(failResult, functionName, outputKey, normalizedEventName, outputTarget, sender, onResult);
                    await eventBinder.HandleExecutionResult(failResult);
                }
            }
        };

        return handler;
    }

    private static void PopulateMetadata(
        ExecutionResult result,
        string functionName,
        string? outputKey,
        string normalizedEventName,
        string? outputTarget,
        object? sender,
        Action<string, string, object?>? onResult)
    {
        result.Metadata ??= new Dictionary<string, object?>();
        result.Metadata["FunctionName"] = functionName;
        result.Metadata["OutputKey"] = outputKey;
        result.Metadata["EventName"] = normalizedEventName;
        result.Metadata["OutputTarget"] = outputTarget;
        result.Metadata["Sender"] = sender;
        result.Metadata["OnResult"] = onResult;
    }

    private static void DeclarationBindingStep(EventBindingContext ctx, IFunctionResolver? functionResolver, IParameterBinder? parameterBinder, IEventBinder? eventBinder, Action<string, string, object?>? onEventResult, ErrorCollector errors,
        Func<string, Dictionary<string, object>, Task>? executeFunctionDelegate = null)
    {
        var declaration = ctx.CurrentDeclaration;
        var control = ctx.CurrentControl;

        if (declaration == null)
            return;

        var resolvedControlName = control != null
            ? EventBindingHelper.GetControlName(control)
            : "(null)";

        if (ctx.EventBus == null)
            throw new InvalidOperationException(
                $"EventBus 未设置，无法绑定控件 '{resolvedControlName}' 的事件。");

        if (functionResolver == null)
            throw new InvalidOperationException(
                $"IFunctionResolver 未设置，无法创建事件处理器。控件 '{resolvedControlName}'。");

        if (eventBinder == null)
            throw new InvalidOperationException(
                $"IEventBinder 未设置，无法绑定事件。控件 '{resolvedControlName}'。");

        if (executeFunctionDelegate == null)
            throw new InvalidOperationException(
                $"ExecuteFunctionDelegate 未设置，无法创建事件处理器。控件 '{resolvedControlName}'，函数 '{declaration.FunctionName}'。");

        if (control == null)
        {
            var result = new BindingResult
            {
                ErrorCount = 1
            };
            ctx.AddSkippedControl(new TConfig.ControlBindingInfo
            {
                ControlName = resolvedControlName,
                Status = "Skipped",
                SkipReason = $"控件 '{resolvedControlName}' 未找到。SourceName='{declaration.SourceName}'"
            });
            return;
        }

        var eventName = declaration.EventType;
        var functionName = declaration.FunctionName;
        var parameterBindings = ctx.CurrentParameterBindings;

        if (string.IsNullOrWhiteSpace(eventName) || string.IsNullOrWhiteSpace(functionName))
        {
            ctx.AddSkippedControl(new TConfig.ControlBindingInfo
            {
                ControlName = resolvedControlName,
                Status = "Skipped",
                SkipReason = $"事件名或函数名为空: eventName='{eventName}', functionName='{functionName}'"
            });
            return;
        }

        if (BindSingleEvent(ctx, control, resolvedControlName, eventName, functionName,
                declaration.OutputTarget, null,
                functionResolver, parameterBinder, eventBinder, onEventResult, errors, executeFunctionDelegate))
        {
            ctx.AddBoundControl(new TConfig.ControlBindingInfo
            {
                ControlName = resolvedControlName,
                ControlType = control.GetType().Name,
                Status = "Bound",
                BoundEventCount = 1
            });
        }
    }

    private static bool BindSingleEvent(
        EventBindingContext ctx,
        object control,
        string resolvedControlName,
        string eventName,
        string functionName,
        string? outputTarget,
        string? outputKey,
        IFunctionResolver? functionResolver,
        IParameterBinder? parameterBinder,
        IEventBinder? eventBinder,
        Action<string, string, object?>? onEventResult,
        ErrorCollector errors,
        Func<string, Dictionary<string, object>, Task>? executeFunctionDelegate = null)
    {
        var normalizer = new DefaultEventNameNormalizer();
        var normalizedEventName = normalizer.ToPascalCase(eventName);

        if (functionResolver == null)
        {
            throw new InvalidOperationException(
                $"IFunctionResolver 未设置，无法创建事件处理器。"
                + $"控件 '{resolvedControlName}'，函数 '{functionName}'。");
        }

        var handler = CreateEventHandler(
            control, normalizedEventName, functionName,
            ctx.CurrentParameterBindings,
            outputTarget: outputTarget,
            outputKey: outputKey,
            onResult: onEventResult,
            parameterBinder: parameterBinder,
            eventBinder: eventBinder,
            executeFunctionDelegate: executeFunctionDelegate);

        if (eventBinder == null || !eventBinder.TryBindEvent(ctx.EventBus, control, normalizedEventName, handler, errors))
        {
            throw new InvalidOperationException(
                $"绑定事件 '{normalizedEventName}' 到控件 '{resolvedControlName}' 失败。"
                + $"函数名: {functionName}。请检查事件名是否与控件支持的事件匹配。");
        }

        return true;
    }

}
