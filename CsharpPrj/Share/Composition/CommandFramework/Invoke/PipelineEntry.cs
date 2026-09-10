
using CommandFramework.Abstractions;
using CommandFramework.Models;
using CommandFramework.Pipeline.Stages;
using CommandFramework.Parsing;
using FrameworkAbstractions.Controls;
using Microsoft.Extensions.Logging;
using BP = Modules.Pipeline;
using IFunctionExecutor = TConfig.IFunctionExecutor;
using TConfig;

using LoadResult = CommandFramework.Models.LoadResult;


namespace CommandFramework.Pipeline
{
    public static class PipelineEntry
    {
        public static BP.IStagePipeline<CommandContext> CreateDefaultPipeline(
            string name = "DefaultCommandPipeline",
            Action<BP.IStagePipeline<CommandContext>>? configure = null,
            CommandParser? parser = null,
            IFunctionExecutor? functionExecutor = null,
            Func<string, string, BindingResult?>? bindingFinder = null,
            INonEventCommandLookup? nonEventCommandLookup = null,
            IControlFinder? controlFinder = null,
            TConfig.Controls.IControlValueAccessor? controlValueWriter = null,
            IParameterExecutionWorkflow? parameterExecutionWorkflow = null,
            object? executionCell = null,
            params BP.IPipelineStage<CommandContext>[] additionalStages)
        {
            var pipeline = new BP.StagePipeline<CommandContext>(name, "Default command execution pipeline");

            var parseStage = new ParseCommandStage(parser ?? new CommandParser());

            var executeStage = new ExecuteCommandStage(
                functionExecutor: functionExecutor,
                bindingFinder: bindingFinder,
                nonEventCommandLookup: nonEventCommandLookup,
                controlFinder: controlFinder,
                controlValueWriter: controlValueWriter,
                parameterExecutionWorkflow: parameterExecutionWorkflow,
                executionCell: executionCell,
                logger: null);



            var resultStage = new ResultCollectStage();

            pipeline
                .Register(parseStage)
                .Register(executeStage)
                .Register(resultStage);

            foreach (var stage in additionalStages)
            {
                pipeline.Register(stage);
            }

            configure?.Invoke(pipeline);

            return pipeline;
        }


    }

    public class PipelineEntryInstance : IDisposable
    {
        private readonly Dictionary<string, object> _loadedConfigs;

        private readonly LinkedList<CommandContext> _history = new();

        private LinkedListNode<CommandContext>? _currentNode;

        private bool _disposed;

        protected readonly CommandParser _parser;
        protected readonly INonEventCommandLookup? _nonEventLookup;
        protected readonly IControlFinder? _controlFinder;
        protected readonly TConfig.Controls.IControlValueAccessor? _controlValueAccessor;
        protected readonly IParameterExecutionWorkflow? _parameterExecutionWorkflow;
        protected readonly object? _executionCell;
        private readonly ILoggerFactory? _loggerFactory;
        private readonly IServiceProvider? _serviceProvider;

        private volatile BP.IStagePipeline<CommandContext>? _cachedDefaultPipeline;
        private readonly object _pipelineCacheLock = new();

        private IConfigLoader? _configLoader;
        private IEventBindingResolver? _bindingResolver;
        private IUIController? _uiController;
        private IStateQuery? _stateQuery;

        public IReadOnlyDictionary<string, object> LoadedConfigs =>
            new Dictionary<string, object>(_loadedConfigs);

        public bool CanUndo => _currentNode != null && _currentNode.Previous != null;

        public bool CanRedo => _currentNode != null && _currentNode.Next != null;

        public int HistoryCount => _history.Count;

        public int MaxHistoryCount { get; set; } = 1000;

        public PipelineEntryInstance(PipelineEntryOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            _parser = options.Parser ?? throw new ArgumentNullException(nameof(options.Parser));
            _nonEventLookup = options.NonEventLookup;
            _controlFinder = options.ControlFinder;
            _controlValueAccessor = options.ControlValueAccessor;
            _parameterExecutionWorkflow = options.ParameterExecutionWorkflow;
            _executionCell = options.ExecutionCell;
            _loggerFactory = options.LoggerFactory;
            _serviceProvider = options.ServiceProvider;
            _loadedConfigs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }


        public void RegisterServices(
            IConfigLoader? configLoader = null,
            IEventBindingResolver? bindingResolver = null,
            IUIController? uiController = null,
            IStateQuery? stateQuery = null)
        {
            _configLoader = configLoader;
            _bindingResolver = bindingResolver;
            _uiController = uiController;
            _stateQuery = stateQuery;
        }


        public LoadResult LoadConfig(string configPath, bool showForm = false)
        {
            if (_configLoader == null)
                return LoadResult.Failure("IConfigLoader is not registered. Call RegisterServices() first.");

            var tConfigResult = _configLoader.LoadConfig(configPath, showForm, _uiController);
            if (tConfigResult.Success)
                return LoadResult.Success(tConfigResult.ConfigName ?? "", configPath, tConfigResult.Config);
            else
                return LoadResult.Failure(tConfigResult.ErrorMessage ?? "Unknown error", configPath);
        }


        public IEventBindingInfo? FindBinding(string controlName, string action)
        {
            var eventConfig = new TConfig.EventConfig
            {
                EventsDictionary = new Dictionary<string, List<string>>
                {
                    [action] = new List<string> { controlName }
                }
            };
            var resolved = _bindingResolver?.Resolve(controlName, eventConfig)?.FirstOrDefault();
            if (resolved == null) return null;

            return new EventBindingInfoWrapper(resolved);
        }

        private sealed class EventBindingInfoWrapper : IEventBindingInfo
        {
            private readonly TConfig.ResolvedEventBinding _inner;

            public EventBindingInfoWrapper(TConfig.ResolvedEventBinding inner)
            {
                _inner = inner;
            }

            public string FunctionName => _inner.FunctionName;
            public string EventType => _inner.EventName;
            public string? Output => null;
            public IReadOnlyList<string> FunctionNames => new[] { _inner.FunctionName };
        }

        public CommandContext CreateContext(string? command = null, int lineNumber = 0)
        {
            var context = new CommandContext
            {
                CurrentCommand = command,
                CurrentCommandLineNumber = lineNumber,
                LoadedConfigs = _loadedConfigs,
                // R-2（8.6）：比较器统一 OrdinalIgnoreCase（对齐 StateManagement.VirtualStore 权威语义）
                VirtualStore = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            };

            if (_loggerFactory != null)
            {
                var logger = _loggerFactory.CreateLogger("CommandFramework");
                context.SharedData["__Logger"] = logger;
            }
            if (_serviceProvider != null)
            {
                context.SharedData["__ServiceProvider"] = _serviceProvider;
            }

            return context;
        }

        private void TrimHistoryAfterCurrent()
        {
            if (_currentNode != null)
            {
                while (_currentNode.Next != null)
                    _history.Remove(_currentNode.Next);
            }
        }

        private void RecordToHistory(CommandContext context)
        {
            _history.AddLast(context);
            while (_history.Count > MaxHistoryCount)
            {
                _history.RemoveFirst();
            }
            _currentNode = _history.Last;
        }

        protected virtual BP.IStagePipeline<CommandContext> GetOrCreateDefaultPipeline()
        {
            if (_cachedDefaultPipeline != null)
                return _cachedDefaultPipeline;

            lock (_pipelineCacheLock)
            {
                if (_cachedDefaultPipeline != null)
                    return _cachedDefaultPipeline;

                _cachedDefaultPipeline = PipelineEntry.CreateDefaultPipeline(
                    parser: _parser,
                    nonEventCommandLookup: _nonEventLookup,
                    controlFinder: _controlFinder,
                    controlValueWriter: _controlValueAccessor,
                    parameterExecutionWorkflow: _parameterExecutionWorkflow,
                    executionCell: _executionCell);
            }

            return _cachedDefaultPipeline;
        }



        private async Task<CommandContext> ExecuteAndRecordAsync(
            string command,
            BP.IStagePipeline<CommandContext> pipeline,
            int lineNumber,
            CancellationToken ct)
        {
            var context = CreateContext(command, lineNumber);
            await pipeline.ExecuteAsync(context, ct);
            RecordToHistory(context);
            return context;
        }

        public async Task<CommandContext> RunCommandAsync(
            string command,
            BP.IStagePipeline<CommandContext>? pipeline = null,
            CancellationToken ct = default)
        {
            pipeline ??= GetOrCreateDefaultPipeline();

            TrimHistoryAfterCurrent();

            return await ExecuteAndRecordAsync(command, pipeline, 1, ct);
        }

        public async Task<List<CommandContext>> RunCommandsAsync(
            IEnumerable<string> commands,
            BP.IStagePipeline<CommandContext>? pipeline = null,
            CancellationToken ct = default)
        {
            var results = new List<CommandContext>();
            pipeline ??= GetOrCreateDefaultPipeline();

            TrimHistoryAfterCurrent();

            int lineNumber = 1;
            foreach (var command in commands)
            {
                ct.ThrowIfCancellationRequested();
                var context = await ExecuteAndRecordAsync(command, pipeline, lineNumber++, ct);
                results.Add(context);
            }

            return results;
        }

        public CommandContext? Undo()
        {
            if (!CanUndo || _currentNode?.Previous == null)
                return null;

            _currentNode = _currentNode.Previous;
            return _currentNode.Value;
        }

        public CommandContext? Redo()
        {
            if (!CanRedo || _currentNode?.Next == null)
                return null;

            _currentNode = _currentNode.Next;
            return _currentNode.Value;
        }

        public void ClearHistory()
        {
            _history.Clear();
            _currentNode = null;
        }

        public Dictionary<string, object> GetLoadedConfigsDictionary() => _loadedConfigs;

        public bool RemoveConfig(string configName)
        {
            if (_configLoader != null)
            {
                var result = _configLoader.RemoveConfig(configName);
                _loadedConfigs.Remove(configName, out _);
                return result;
            }

            return _loadedConfigs.Remove(configName, out _);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_configLoader is IDisposable disposableConfigLoader)
                disposableConfigLoader.Dispose();

            _history.Clear();
            _currentNode = null;
            _loadedConfigs.Clear();
        }
    }

}
