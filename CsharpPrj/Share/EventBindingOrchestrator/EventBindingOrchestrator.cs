
using System.Linq;
using CSG.Unified;
using Microsoft.Extensions.Logging;
using Modules.ConfigurationModels;
using Modules.Pipeline;
using Modules.Pipeline.StaticPipeline;
using Modules.EventFramework.Abstractions;
using Modules.EventFramework.Services;
using IEventTriggerResult = Modules.EventFramework.Services.IEventTriggerResult;
using Share.EventBindingOrchestrator.Binding;
using Share.EventBindingOrchestrator.Diagnostics;
using Share.EventBindingOrchestrator.Services;
using Share.EventBindingOrchestrator.Transformers;
using Share.EventBindingOrchestrator.Parameters;
using Share.EventBindingOrchestrator.Utils;
using FrameworkAbstractions.ComponentRegistry;
using TConfig;


namespace Share.EventBindingOrchestrator
{
    public class EventBindingOrchestrator : TConfig.IEventBindingService
    {

        private readonly ILogger<EventBindingOrchestrator> _logger;
        private readonly EventBindingOptions _options;

        private readonly IFunctionResolver? _functionResolver;
        private readonly IParameterBinder? _parameterBinder;
        private readonly IEventBinder? _eventBinder;

        private readonly StaticPipeline<EventBindingContext> _unifiedPipeline;

        private readonly ErrorCollector _globalStats = new();

        private readonly IEventBindingDiagnostics? _diagnostics;


        public EventBindingOrchestrator(
            ILogger<EventBindingOrchestrator> logger,
            IFunctionResolver? functionResolver = null,
            IParameterBinder? parameterBinder = null,
            IEventBinder? eventBinder = null,
            EventBindingOptions? options = null,
            IEventBindingDiagnostics? diagnostics = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _functionResolver = functionResolver;
            _parameterBinder = parameterBinder;
            _eventBinder = eventBinder;
            _options = options ?? new EventBindingOptions();
            _diagnostics = diagnostics;

            _unifiedPipeline = EventBindingPipelineFactory.CreateFullUnifiedPipeline(
                logger, _globalStats,
                functionResolver, parameterBinder, eventBinder,
                options?.OnEventResult,
                defaultOptions: _options);
        }

        public EventBindingOrchestrator(
            IFunctionResolver? functionResolver = null,
            IParameterBinder? parameterBinder = null,
            IEventBinder? eventBinder = null,
            EventBindingOptions? options = null)
            : this(
                logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<EventBindingOrchestrator>.Instance,
                functionResolver: functionResolver,
                parameterBinder: parameterBinder,
                eventBinder: eventBinder,
                options: options ?? new EventBindingOptions(),
                diagnostics: null)
        {
        }


        public EventBindingContext ExecuteWithWalk(
            object targetObject,
            UIConfiguration config,
            EventBindingOptions? options = null)
        {
            if (targetObject == null) throw new ArgumentNullException(nameof(targetObject));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var effectiveOptions = options ?? _options;
            var context = new EventBindingContext
            {
                TargetObject = targetObject,
                Config = config
            };
            context.ApplyOptions(effectiveOptions);

            EventBindingDiagnosticContext.Record(EventNames.PipelineExecuteWithWalkStart, new
            {
                targetType = targetObject.GetType().Name,
                viewCount = config.Views?.Count ?? 0,
                hasSearchRoot = effectiveOptions.SearchRoot != null
            });

            _unifiedPipeline.Execute(context);

            EventBindingDiagnosticContext.Record(EventNames.PipelineExecuteWithWalkComplete, new
            {
                boundCount = context.BoundCount,
                skippedCount = context.SkippedCount,
                message = $"事件绑定完成: {context.BoundCount} 成功, {context.SkippedCount} 跳过"
            });

            if (_diagnostics != null)
            {
                _diagnostics.CaptureFromContext(context);
            }

            return context;
        }


        public BindingResult LoadAndBindDeclarations(
            IReadOnlyList<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>> declarations,
            object targetObject)
        {
            if (declarations == null)
                throw new ArgumentNullException(nameof(declarations));
            if (targetObject == null)
                throw new ArgumentNullException(nameof(targetObject));

            EventBindingDiagnosticContext.Record(EventNames.OrchestratorLoadAndBindStart, new
            {
                declarationCount = declarations.Count,
                targetType = targetObject.GetType().Name
            });

            try
            {
                var allBoundControls = new List<TConfig.ControlBindingInfo>();
                var allSkippedControls = new List<TConfig.ControlBindingInfo>();

                 foreach (var declaration in declarations)
                 {
                     var declContext = new EventBindingContext
                     {
                         CurrentDeclaration = declaration,
                         TargetObject = targetObject
                     };

                     declContext.ApplyOptions(_options);

                    _unifiedPipeline.Execute(declContext);

                    allBoundControls.AddRange(declContext.BoundControls);
                    allSkippedControls.AddRange(declContext.SkippedControls);
                }

                var bindingResult = new BindingResult
                {
                    BoundControls = allBoundControls,
                    SkippedControls = allSkippedControls
                };

                bindingResult.ErrorCount = allSkippedControls.Count(
                    s => string.Equals(s.Status, "Error", StringComparison.OrdinalIgnoreCase));

                _globalStats.Add("STATS_BOUND_COUNT_DECL", bindingResult.BoundCount.ToString(), ErrorLevel.Info);
                _globalStats.Add("STATS_SKIPPED_COUNT_DECL", bindingResult.SkippedCount.ToString(), ErrorLevel.Info);
                _globalStats.Add("STATS_ERROR_COUNT_DECL", bindingResult.ErrorCount.ToString(), ErrorLevel.Info);

                EventBindingDiagnosticContext.Record("orchestrator.loadandbind.complete", new
                {
                    boundCount = bindingResult.BoundCount,
                    skippedCount = bindingResult.SkippedCount,
                    message = $"事件绑定完成（统一管线）: {bindingResult.BoundCount} 成功, {bindingResult.SkippedCount} 跳过"
                });

                return bindingResult;
            }
            catch (Exception ex)
            {
                return CreateErrorResult(ex, "事件绑定失败（统一管线）");
            }
        }

        public BindingResult LoadAndBindFromConfig(
            UIConfiguration config,
            object targetObject)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (targetObject == null)
                throw new ArgumentNullException(nameof(targetObject));

            var declarations = EventBindingDeclarationFactory.Build(config);
            return LoadAndBindDeclarations(declarations, targetObject);
        }


        private BindingResult CreateErrorResult(Exception ex, string logPrefix)
        {
            EventBindingDiagnosticContext.Record("orchestrator.createerrorresult", new
            {
                prefix = logPrefix,
                error = ex.Message,
                stackTrace = ex.StackTrace,
                message = $"{logPrefix}: {ex.Message}"
            });

            var errorResult = new BindingResult
            {
                ErrorCount = 1
            };

            _globalStats.Add("STATS_ERROR_COUNT", "1", ErrorLevel.Error);

            return errorResult;
        }


        public bool TryGetBoundCount(out int count)
        {
            var boundEntries = _globalStats.GetErrorsByLevel(ErrorLevel.Info)
                .Where(e => e.Code == "STATS_BOUND_COUNT" || e.Code == "STATS_BOUND_COUNT_DECL")
                .ToList();

            count = boundEntries.Sum(e => int.TryParse(e.Message, out var c) ? c : 0);
            return boundEntries.Count > 0;
        }

        public Task<EventBindingContext> ExecuteWithWalkAsync(
            object targetObject,
            UIConfiguration config,
            object? searchRoot = null)
        {
            var options = _options.Clone();
            if (searchRoot != null)
            {
                options.SearchRoot = searchRoot;
            }

            options.EventBus ??= AppServiceProvider.Resolve<IEventBus>();

            return Task.FromResult(ExecuteWithWalk(targetObject, config, options));
        }

        public Dictionary<string, string> GetBindingSummary()
        {
            var summary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in _globalStats.Errors)
            {
                if (entry.Level != ErrorLevel.Info && entry.Level != ErrorLevel.Warning && entry.Level != ErrorLevel.Error)
                    continue;

                if (entry.Code == "STATS_BOUND_COUNT_DECL")
                {
                    summary["__total_bound__"] = $"BoundCount={entry.Message}";
                }
                else if (entry.Code == "STATS_SKIPPED_COUNT_DECL")
                {
                    summary["__total_skipped__"] = $"SkippedCount={entry.Message}";
                }
                else if (entry.Code == "EVENT_BIND_FAILED" || entry.Code == "EVENT_BIND_EVENTBUS_FAILED")
                {
                    var controlHint = ParseControlFromMessage(entry.Message);
                    if (controlHint != null)
                    {
                        summary[controlHint] = $"Error: {entry.Message}";
                    }
                    else
                    {
                        summary[$"__error_{entry.Code}__"] = $"Error: {entry.Message}";
                    }
                }
            }

            return summary;
        }

        private static string? ParseControlFromMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return null;

            const string marker = "control=";
            var idx = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;

            var start = idx + marker.Length;
            var end = message.IndexOf(',', start);
            var controlName = end < 0
                ? message.Substring(start).Trim()
                : message.Substring(start, end - start).Trim();

            return string.IsNullOrEmpty(controlName) ? null : controlName;
        }

        public void ResetStatistics()
        {
            _globalStats.Clear();
        }

        public Task UnbindEventsAsync(object target)
        {
            if (target == null)
                return Task.CompletedTask;

            var eventBus = _options.EventBus;
            if (eventBus == null)
                return Task.CompletedTask;

            try
            {
                eventBus.UnbindControlEvent(target, eventName: null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UnbindEventsAsync failed for target={TargetType}", target.GetType().Name);
            }

            return Task.CompletedTask;
        }


        public TConfig.IEventBindingResolver BindingResolver { get; internal set; } = null!;

        public Task<BindingResult> BindControlAsync(object control, EventConfig eventConfig)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            if (eventConfig == null) throw new ArgumentNullException(nameof(eventConfig));

            var config = new UIConfiguration
            {
                Views = new List<ViewConfig>
                {
                    new ViewConfig
                    {
                        Controls = new List<ControlConfig>
                        {
                            new ControlConfig
                            {
                                Name = control.GetType().Name,
                                Events = eventConfig
                            }
                        }
                    }
                }
            };

            var context = ExecuteWithWalk(control, config);
            return Task.FromResult(new BindingResult
            {
                BoundControls = context.BoundControls.ToList(),
                SkippedControls = context.SkippedControls.ToList()
            });
        }

        public async Task<List<BindingResult>> BindControlsAsync(
            IEnumerable<object> controls, IEnumerable<EventConfig> eventConfigs)
        {
            if (controls == null) throw new ArgumentNullException(nameof(controls));
            if (eventConfigs == null) throw new ArgumentNullException(nameof(eventConfigs));

            var results = new List<BindingResult>();
            var controlList = controls.ToList();
            var configList = eventConfigs.ToList();

            for (int i = 0; i < Math.Min(controlList.Count, configList.Count); i++)
            {
                var result = await BindControlAsync(controlList[i], configList[i]);
                results.Add(result);
            }

            return results;
        }


        public Task<IEventTriggerResult> TriggerEventAsync(
            string controlName, string eventType, string value)
        {
            if (_options.SearchRoot == null)
            {
                throw new InvalidOperationException(
                    $"TriggerEventAsync 失败: SearchRoot 未设置。无法查找控件 '{controlName}'。请确保 EventBindingOptions.SearchRoot 已设置。");
            }

            var context = new EventBindingContext
            {
                TargetObject = _options.SearchRoot,
                SearchRoot = _options.SearchRoot,
                CurrentDeclaration = new EventBindingDeclaration<IReadOnlyList<ParameterBinding>>
                {
                    SourceName = controlName,
                    EventType = eventType
                }
            };

            _unifiedPipeline.Execute(context);

            if (context.CurrentControl == null)
            {
                return Task.FromResult<IEventTriggerResult>(Models.EventTriggerResult.FailureResult(
                    $"控件 '{controlName}' 未找到（SearchRoot={_options.SearchRoot.GetType().Name}）",
                    eventName: eventType));
            }

            return Task.FromResult<IEventTriggerResult>(
                context.BoundCount > 0
                    ? Models.EventTriggerResult.SuccessResult(value: value, eventName: eventType)
                    : Models.EventTriggerResult.FailureResult(
                        $"事件触发失败: 控件 '{controlName}' 的事件 '{eventType}' 未绑定（所有触发策略均失败）",
                        eventName: eventType));
        }

        public Task<BindingResult> BindEventsAsync(UIConfiguration config, object? options = null)
        {
            if (_options.SearchRoot == null)
            {
                throw new InvalidOperationException(
                    "BindEventsAsync 失败: SearchRoot 未设置。请确保 EventBindingOptions.SearchRoot 已设置。");
            }

            var targetObject = _options.SearchRoot;
            var eventOptions = options as EventBindingOptions;

            var context = ExecuteWithWalk(targetObject, config, eventOptions);

            return Task.FromResult(new BindingResult
            {
                BoundControls = context.BoundControls.ToList(),
                SkippedControls = context.SkippedControls.ToList()
            });
        }
    }
}
