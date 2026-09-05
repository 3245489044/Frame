
using System.Threading;
using BasicMethod.Declarative;
using Framework.Tree;
using Modules.Pipeline.StaticPipeline;
using TConfig;
using Share.EventBindingOrchestrator.Utils;
using ParameterMapping = TConfig.ParameterMapping;

namespace Share.EventBindingOrchestrator.Parameters
{
    public class EventBindingResolver :
        IEventBindingResolver,
        IDeclarationResolver<IDictionary<string, object>, EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>
    {
        private readonly IDictionary<string, object> _loadedConfigs;

        private readonly TreeIndex<ControlConfig, string> _controlIndex;

        private readonly ReaderWriterLockSlim _indexLock = new();

        private int _indexBuilt;

        private readonly StaticPipeline<ResolverContext> _resolvePipeline;
        private readonly StaticPipeline<ResolverContext> _resolveAllPipeline;
        private readonly StaticPipeline<ResolverContext> _lookupPipeline;

        public EventBindingResolver(IDictionary<string, object> loadedConfigs)
        {
            if (loadedConfigs == null)
                throw new ArgumentNullException(nameof(loadedConfigs));

            _loadedConfigs = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object>(
                loadedConfigs);

            _controlIndex = new TreeIndex<ControlConfig, string>(
                StringComparer.OrdinalIgnoreCase);

            _resolvePipeline = new StaticPipeline<ResolverContext>(new[]
            {
                new Step<ResolverContext>("ConfigIndexBuild", ctx => ConfigIndexBuildStep(ctx, _controlIndex, _indexLock)),
                new Step<ResolverContext>("BindingDeclarationExtract", BindingDeclarationExtractStep),
            });

            _resolveAllPipeline = new StaticPipeline<ResolverContext>(new[]
            {
                new Step<ResolverContext>("MergeSources", MergeSourcesStep),
                new Step<ResolverContext>("ConfigIndexBuild", ctx => ConfigIndexBuildStep(ctx, _controlIndex, _indexLock)),
                new Step<ResolverContext>("BatchBindingDeclarationExtract", BatchBindingDeclarationExtractStep),
            });

            _lookupPipeline = new StaticPipeline<ResolverContext>(new[]
            {
                new Step<ResolverContext>("ControlBindingLookup", ControlBindingLookupStep),
            });
        }

        public static EventBindingResolver FromLoadedConfigs(IDictionary<string, LoadedConfigInfo> loadedConfigs)
        {
            if (loadedConfigs == null)
                throw new ArgumentNullException(nameof(loadedConfigs));

            var dict = loadedConfigs.ToDictionary(
                kvp => kvp.Key,
                kvp => (object)kvp.Value,
                StringComparer.OrdinalIgnoreCase);

            return new EventBindingResolver(dict);
        }


        string IDeclarationResolver<IDictionary<string, object>, EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>.Name => "EventBindingResolver";
        ResolutionResult<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>> IDeclarationResolver<IDictionary<string, object>, EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>.Resolve(
            IDictionary<string, object> source)
        {
            if (source == null)
                return ResolutionResult<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>.Failure("配置源不能为空");

            try
            {
                var ctx = new ResolverContext
                {
                    Source = source,
                    ControlIndex = _controlIndex,
                    IndexLock = _indexLock
                };

                _resolvePipeline.Execute(ctx);

                return ctx.Declaration != null
                    ? ResolutionResult<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>.Success(ctx.Declaration)
                    : ResolutionResult<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>.Failure("未找到任何事件绑定声明");
            }
            catch (Exception ex)
            {
                return ResolutionResult<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>.Failure($"解析失败: {ex.Message}");
            }
        }

        ResolutionResult<IReadOnlyList<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>> IDeclarationResolver<IDictionary<string, object>, EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>.ResolveAll(
            IEnumerable<IDictionary<string, object>> sources)
        {
            if (sources == null)
                return ResolutionResult<IReadOnlyList<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>>.Failure("配置源集合不能为空");

            try
            {
                var sourcesList = sources.ToList();
                if (sourcesList.Count == 0)
                    return ResolutionResult<IReadOnlyList<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>>.Success(
                        new List<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>());

                var ctx = new ResolverContext
                {
                    Source = new Dictionary<string, object>(sourcesList[0]),
                    MergeSources = sourcesList,
                    ControlIndex = _controlIndex,
                    IndexLock = _indexLock
                };

                _resolveAllPipeline.Execute(ctx);

                return ResolutionResult<IReadOnlyList<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>>.Success(ctx.AllDeclarations);
            }
            catch (Exception ex)
            {
                return ResolutionResult<IReadOnlyList<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>>>.Failure($"批量解析失败: {ex.Message}");
            }
        }


        public IEnumerable<ResolvedEventBinding> Resolve(object control, EventConfig eventConfig)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            if (eventConfig == null) throw new ArgumentNullException(nameof(eventConfig));

            var results = new List<ResolvedEventBinding>();

            foreach (var kv in eventConfig.EventsDictionary)
            {
                var eventName = kv.Key;
                var functionNames = kv.Value;
                if (functionNames == null) continue;

                foreach (var functionName in functionNames)
                {
                    if (string.IsNullOrWhiteSpace(functionName)) continue;

                    results.Add(new ResolvedEventBinding
                    {
                        EventName = eventName,
                        FunctionName = functionName
                    });
                }
            }

            return results;
        }

        public string? GetSourceType(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
                return null;

            EnsureIndexBuilt();

            var control = FindControlByName(sourceName);
            return control?.Type;
        }

        public string ResolveEventType(string sourceName, string operationType)
        {
            var sourceType = GetSourceType(sourceName);
            return operationType;
        }

        public IEventBindingInfo? FindEventBinding(string sourceName, string? sourceType = null)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
                return null;

            EnsureIndexBuilt();

            var ctx = new ResolverContext
            {
                ControlName = sourceName,
                EventType = sourceType ?? "",
                ControlIndex = _controlIndex,
                IndexLock = _indexLock
            };

            _lookupPipeline.Execute(ctx);

            return ctx.BindingInfo;
        }

        public string? FindEventFunction(string sourceName, string? sourceType = null)
        {
            var binding = FindEventBinding(sourceName, sourceType);
            return binding?.FunctionName;
        }

        public IDictionary<string, object>? GetLoadedConfigsDictionary()
        {
            return _loadedConfigs;
        }


        private ControlConfig? FindControlByName(string controlName)
        {
            if (_controlIndex.TryGet(controlName, out var control))
                return control;
            return null;
        }


        private void EnsureIndexBuilt()
        {
            if (_indexBuilt != 0)
                return;

            if (_loadedConfigs.Count == 0)
                return;

            if (Interlocked.CompareExchange(ref _indexBuilt, 1, 0) != 0)
                return;

            try
            {
                BuildIndexCore(_loadedConfigs, _controlIndex, _indexLock);
            }
            catch
            {
                Interlocked.Exchange(ref _indexBuilt, 0);
                throw;
            }
        }


        private static void MergeSourcesStep(ResolverContext ctx)
        {
            if (ctx.MergeSources == null || ctx.MergeSources.Count <= 1)
                return;

            for (int i = 1; i < ctx.MergeSources.Count; i++)
            {
                foreach (var kvp in ctx.MergeSources[i])
                {
                    ctx.Source.TryAdd(kvp.Key, kvp.Value);
                }
            }
        }

        private static void ConfigIndexBuildStep(ResolverContext ctx, TreeIndex<ControlConfig, string> controlIndex, ReaderWriterLockSlim indexLock)
        {
            BuildIndexCore(ctx.Source, controlIndex, indexLock);

            ctx.ControlConfigs = controlIndex.GetAll()
                .Select(kvp => kvp.Value)
                .ToList();
        }

        private static void BuildIndexCore(
            IDictionary<string, object> source,
            TreeIndex<ControlConfig, string> controlIndex,
            ReaderWriterLockSlim indexLock)
        {
            indexLock.EnterWriteLock();
            try
            {
                controlIndex.Clear();

                foreach (var kvp in source)
                {
                    TraverseConfigForControls(kvp.Value, controlIndex);
                }
            }
            finally
            {
                indexLock.ExitWriteLock();
            }
        }

        private static void BindingDeclarationExtractStep(ResolverContext ctx)
        {
            var match = ctx.ControlConfigs
                .Where(c => c.Events?.EventsDictionary != null)
                .SelectMany(c => c.Events!.EventsDictionary
                    .Where(kv => kv.Value?.Count > 0 && !string.IsNullOrEmpty(kv.Value[0]))
                    .Select(kv => new
                    {
                        ControlConfig = c,
                        EventName = kv.Key,
                        FunctionName = kv.Value![0]
                    }))
                .FirstOrDefault();

            if (match != null)
            {
                var bindings = match.ControlConfig.Events!.Bindings;
                ctx.Declaration = new EventBindingDeclaration<IReadOnlyList<ParameterBinding>>
                {
                    Name = match.ControlConfig.Name ?? string.Empty,
                    SourceName = match.ControlConfig.Name ?? string.Empty,
                    EventType = match.EventName,
                    FunctionName = match.FunctionName,
                    // D6 收敛：配置参数 DTO 直通（不再经 ParameterMapping 转换）
                    Parameters = bindings?.Parameters is { Count: > 0 }
                        ? bindings.Parameters
                        : Array.Empty<ParameterBinding>(),
                    OutputTarget = bindings?.OutputTarget is { Length: > 0 } outStr ? outStr : null
                };
            }
        }

        private static void BatchBindingDeclarationExtractStep(ResolverContext ctx)
        {
            foreach (var controlConfig in ctx.ControlConfigs)
            {
                if (controlConfig?.Events?.Bindings == null ||
                    controlConfig.Events.EventsDictionary == null)
                    continue;

                var bindings = controlConfig.Events.Bindings;
                IReadOnlyList<ParameterBinding> parameters = bindings.Parameters is { Count: > 0 }
                    ? bindings.Parameters
                    : Array.Empty<ParameterBinding>();

                foreach (var kv in controlConfig.Events.EventsDictionary)
                {
                    var functionNames = kv.Value;
                    if (functionNames == null) continue;

                    foreach (var functionName in functionNames)
                    {
                        if (string.IsNullOrWhiteSpace(functionName)) continue;

                        ctx.AllDeclarations.Add(new EventBindingDeclaration<IReadOnlyList<ParameterBinding>>
                        {
                            Name = controlConfig.Name ?? string.Empty,
                            SourceName = controlConfig.Name ?? string.Empty,
                            EventType = kv.Key,
                            FunctionName = functionName,
                            Parameters = parameters,
                            OutputTarget = bindings.OutputTarget
                        });
                    }
                }
            }
        }

        private static void ControlBindingLookupStep(ResolverContext ctx)
        {
            if (string.IsNullOrWhiteSpace(ctx.ControlName) || string.IsNullOrWhiteSpace(ctx.EventType))
                return;

            if (!ctx.ControlIndex.TryGet(ctx.ControlName, out var control))
                return;

            ctx.BindingInfo = GetBindingInfo(control, ctx.EventType);
        }

        private static IEventBindingInfo? GetBindingInfo(ControlConfig control, string eventType)
        {
            var functionName = control.Events?.EventsDictionary != null
                && control.Events.EventsDictionary.TryGetValue(eventType, out var funcNames)
                && funcNames?.Count > 0
                ? funcNames[0]
                : null;
            if (functionName == null) return null;

            var output = control.Events?.Bindings?.OutputTarget is string outStr
                && !string.IsNullOrEmpty(outStr)
                ? outStr
                : null;

            return new EventBindingInfo
            {
                FunctionNames = new List<string> { functionName },
                EventType = eventType,
                Output = output
            };
        }

        private static void TraverseConfigForControls(object config, TreeIndex<ControlConfig, string> controlIndex)
        {
            UIConfiguration? uiConfig = null;

            if (config is LoadedConfigInfo loadedInfo)
            {
                uiConfig = loadedInfo.Config;
            }
            else if (config is UIConfiguration directConfig)
            {
                uiConfig = directConfig;
            }

            if (uiConfig == null)
                return;

            ControlTreeWalker.WalkAllViews(uiConfig, ctrl =>
            {
                if (!string.IsNullOrWhiteSpace(ctrl.Name))
                {
                    controlIndex.RegisterNode(ctrl, ctrl.Name);
                }
            });
        }
    }

    public class LoadedConfigInfo
    {
        public UIConfiguration Config { get; }

        public string? WindowName { get; set; }

        public DateTime LoadedAt { get; } = DateTime.Now;

        public LoadedConfigInfo(UIConfiguration config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            WindowName = config.Form?.Title;
        }
    }
}
