
#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TConfig;
using Modules.Pipeline;
using TConfig.Abstractions;
using UniversalMiddleware.Abstractions;
using UniversalMiddleware.Messaging;
using UniversalMiddleware.Binding;
using UniversalMiddleware.Configuration;
using UniversalMiddleware.Container;
using UniversalMiddleware.Container.Lifecycle;
using UniversalMiddleware.Controls;


using UniversalMiddleware.Core;
using UniversalMiddleware.Diagnostics;
using UniversalMiddleware.Engine;
using UniversalMiddleware.Extensions;
using UniversalMiddleware.Layout;
using UniversalMiddleware.Middleware;
using UniversalMiddleware.Navigation;
using UniversalMiddleware.Rendering;
using UniversalMiddleware.Rendering.Lifecycle;
using UniversalMiddleware.Serialization;
using UniversalMiddleware.Transformation;
using UniversalMiddleware.Validator;
using CommandFramework.Models;
using CommandFramework.Parsing;
using UniversalMiddleware.EventMapping;
using UniversalMiddleware.Stages;
using ContainerEventManager = UniversalMiddleware.Container.ContainerEventManager;
using JP = Plate.JsonMethod;

namespace UniversalMiddleware;

public class UniversalMiddlewareEntry : IDisposable
{

    private static readonly Lazy<UniversalMiddlewareEntry> _instance = new(() => new UniversalMiddlewareEntry());

    public static UniversalMiddlewareEntry Instance => _instance.Value;


    public static string ToJson<T>(T obj, bool writeIndented = true)
        => Instance._serializer.Value.Serialize(obj, writeIndented ? UnifiedJsonSerializer.IndentedOptions : UnifiedJsonSerializer.DefaultOptions);

    public static T? FromJson<T>(string json, JsonSerializerOptions? options = null)
        => Instance._serializer.Value.Deserialize<T>(json, options);

    public static T? DeepClone<T>(T obj)
        => Instance._serializer.Value.Deserialize<T>(Instance._serializer.Value.Serialize(obj));

    public static string PrettyPrint(string json) => JP.Extensions.JsonExtensions.PrettyPrintJson(json);

    public static string Minify(string json) => JP.Extensions.JsonExtensions.MinifyJson(json);

    public static bool IsValidJson(string json)
    {
        try { JsonDocument.Parse(json); return true; }
        catch { return false; }
    }


    private readonly Lazy<UnifiedJsonSerializer> _serializer;
    private readonly Lazy<JsonValidator> _validator;
    private readonly Lazy<JsonPathEngine> _pathEngine;
    private readonly Lazy<Mediator> _mediator;
    private readonly Lazy<TConfig.IEventBus> _eventBus;
    private readonly Lazy<TConfig.ICommandExecutor> _commandExecutor;
    private readonly Lazy<DataBindingManager> _dataBinding;
    private readonly Lazy<SmartLayoutEngine> _layoutEngine;
    private readonly Lazy<LayoutCalculator> _layoutCalculator;
    private readonly Lazy<LayoutValidator> _layoutValidator;
    private readonly Lazy<ContainerManager> _containerManager;
    private readonly Lazy<ConsoleLogger> _logger;
    private readonly Lazy<SessionManager> _sessionManager;
    private readonly Lazy<GenericControlFactory> _controlFactory;
    private readonly Lazy<IEventMappingService> _eventMapping;
    private Lazy<TConfig.IConfigLoader> _configLoader;

    private bool _initialized;
    private bool _disposed;
    private readonly object _lock = new();


    public Abstractions.IJsonSerializer Serializer => _serializer.Value;

    public Abstractions.IJsonValidator Validator => _validator.Value;

    public Abstractions.IJsonPathEngine PathEngine => _pathEngine.Value;

    public JP.Utilities.JsonPreprocessor Preprocessor => UnifiedJsonSerializer.Preprocessor;

    public Abstractions.IMediator Mediator => _mediator.Value;

    public TConfig.IEventBus EventBus => _eventBus.Value;

    public ICacheManager Cache => _mediator.Value.Cache;

    public IStateManager State => _mediator.Value.State;

    public TConfig.ICommandExecutor Commands => _commandExecutor.Value;

    public IDataBindingManager DataBinding => _dataBinding.Value;

    public ISmartLayoutEngine Layout => _layoutEngine.Value;

    public ILayoutCalculator LayoutCalculator => _layoutCalculator.Value;

    public ILayoutValidator LayoutValidator => _layoutValidator.Value;

    public IContainerManager Containers => _containerManager.Value;

    public SessionManager Sessions => _sessionManager.Value;

    public IConfigDrivenFactory<ControlDescriptor, object> ControlFactory => _controlFactory.Value;

    public Microsoft.Extensions.Logging.ILogger Logger => _logger.Value;

    public IEventMappingService EventMapping => _eventMapping.Value;

    public TConfig.IConfigLoader? ConfigLoader => _configLoader.IsValueCreated ? _configLoader.Value : null;

    public void RegisterConfigLoader(TConfig.IConfigLoader configLoader)
    {
        if (_configLoader.IsValueCreated)
            throw new InvalidOperationException("ConfigLoader has already been registered");
        _configLoader = new Lazy<TConfig.IConfigLoader>(() => configLoader);
    }

    public bool IsInitialized => _initialized;


    public UniversalMiddlewareEntry(Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null, string categoryName = "Universal")
    {
        _logger = new Lazy<ConsoleLogger>(() =>
        {
            if (loggerFactory != null)
                return new ConsoleLogger(loggerFactory, categoryName);
            return new ConsoleLogger(categoryName);
        });

        _serializer = new Lazy<UnifiedJsonSerializer>(() => new UnifiedJsonSerializer());
        _validator = new Lazy<JsonValidator>(() => new JsonValidator());
        _pathEngine = new Lazy<JsonPathEngine>(() => new JsonPathEngine());

        _mediator = new Lazy<Mediator>(() => new Mediator(
            cache: new CacheManager(),
            state: new StateManager(),
            logger: (Microsoft.Extensions.Logging.ILogger)_logger.Value
        ));

        _eventBus = new Lazy<TConfig.IEventBus>(() => new Modules.EventFramework.Bus.DefaultEventBus());
        _commandExecutor = new Lazy<TConfig.ICommandExecutor>(() => new Modules.EventFramework.Bus.DefaultCommandExecutor());
        _dataBinding = new Lazy<DataBindingManager>(() => new DataBindingManager());

        _layoutEngine = new Lazy<SmartLayoutEngine>(() => new SmartLayoutEngine(
            calculator: null,
            validator: null,
            logger: (Microsoft.Extensions.Logging.ILogger)_logger.Value
        ));

        _layoutCalculator = new Lazy<LayoutCalculator>(() => new LayoutCalculator());
        _layoutValidator = new Lazy<LayoutValidator>(() => new LayoutValidator());
        _containerManager = new Lazy<ContainerManager>(() => new ContainerManager());
        _sessionManager = new Lazy<SessionManager>(() => new SessionManager());
        _controlFactory = new Lazy<GenericControlFactory>(() => new GenericControlFactory((Microsoft.Extensions.Logging.ILogger)_logger.Value));

        _eventMapping = new Lazy<IEventMappingService>(() => new EventMappingService());

        _configLoader = new Lazy<TConfig.IConfigLoader>(() => null!);
    }


    public void Initialize()
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;

            JsonConverterRegistrar.RegisterAll();


            _initialized = true;
            _logger.Value.LogInformation("UniversalMiddleware initialized");
        }
    }


    public JsonPipelineBuilder<TInput, TOutput> CreatePipeline<TInput, TOutput>(string name = "JsonPipeline")
        => new JsonPipelineBuilder<TInput, TOutput>(name);

    public JsonPipelineBuilder<string, TTarget> CreateJsonPipeline<TTarget>(string name = "JsonToModel")
        => new JsonPipelineBuilder<string, TTarget>(name)
            .AddTransform(_ => new JsonParseTransform())
            .AddTransform(_ => new JsonValidateTransform())
            .AddTransform(_ => new JsonDeserializeTransform<TTarget>());

    public JsonPipelineBuilder<string, TTarget> CreateFilePipeline<TTarget>(string name = "FileToModel")
        => new JsonPipelineBuilder<string, TTarget>(name)
            .AddTransform(_ => new JsonFileLoadTransform())
            .AddTransform(_ => new JsonParseTransform())
            .AddTransform(_ => new JsonValidateTransform())
            .AddTransform(_ => new JsonDeserializeTransform<TTarget>());


    public MiddlewarePipeline CreateMiddleware(string name = "Middleware")
        => new MiddlewarePipeline();

    public MiddlewarePipeline CreateLoggingMiddleware(string name = "LoggingMiddleware")
    {
        var pipeline = new MiddlewarePipeline();
        pipeline.Use(async (cmd, next) =>
        {
            _logger.Value.LogInformation($"[{name}] Executing: {cmd.Action ?? cmd.Verb.ToString()}");
            try
            {
                var result = await next(cmd);
                _logger.Value.LogInformation($"[{name}] Completed: {result.IsSuccess}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.Value.LogError(ex, $"[{name}] Failed: {ex.Message}");
                return JsonCommandResult.Fail(JsonErrorCode.UnknownError, ex.Message);
            }
        });
        return pipeline;
    }


    public RenderPipelineBase<TTarget> CreateRenderPipeline<TTarget>(
        string name,
        Func<IRenderSession, Task>? createAction = null,
        Func<IRenderSession, Task>? layoutAction = null,
        Func<IRenderSession, Task>? bindAction = null)
    {
        return new DelegateRenderPipeline<TTarget>(name, createAction, layoutAction, bindAction, (Microsoft.Extensions.Logging.ILogger)_logger.Value);
    }

    [Obsolete("请使用 BackEndDriven.Pipeline.RenderPipelineOrchestrator 替代 ContainerOrchestrator")]
    public ContainerOrchestrator CreateOrchestrator(IRenderPipeline pipeline, string? name = null)
        => new ContainerOrchestrator(pipeline, name);


    public ConfigRenderPipeline<TContainer> CreateConfigPipeline<TContainer>(string name = "ConfigPipeline")
        where TContainer : IConfigContainer
    {
        if (_configLoader.IsValueCreated && _configLoader.Value != null)
        {
            return new DefaultConfigRenderPipeline<TContainer>(_configLoader.Value, name);
        }
        throw new InvalidOperationException("ConfigLoader has not been registered. Call RegisterConfigLoader() first.");
    }


    public Abstractions.LayoutPosition ConvertLayoutPosition(TConfig.LayoutPosition src)
        => LayoutPositionConverter.Convert(src);

    public LayoutDefinition ConvertToLayoutDefinition(IEnumerable<ControlConfig> configs, string name = "AutoGenerated")
        => LayoutPositionConverter.ToLayoutDefinition(configs, name);




    public ContainerNode CreateContainerNode(string name, IConfigContainer container)
        => new ContainerNode(name, container);

    public ContainerLifecycle CreateLifecycle()
        => new ContainerLifecycle();

    public ContainerDisposer CreateDisposer(ContainerLifecycle lifecycle, ContainerNode node)
        => new ContainerDisposer(lifecycle, node);

    public Container.Events.EventPropagator CreateEventPropagator(ContainerEventManager eventManager)
        => new Container.Events.EventPropagator(eventManager);


    public UiConfigBuilder ConfigureUI()
        => new UiConfigBuilder();


    public JsonParseTransform CreateParseTransform() => new JsonParseTransform();

    public JsonValidateTransform CreateValidateTransform() => new JsonValidateTransform();

    public JsonDeserializeTransform<T> CreateDeserializeTransform<T>(Abstractions.IJsonSerializer? serializer = null)
        => new JsonDeserializeTransform<T>(serializer);

    public JsonFileLoadTransform CreateFileLoadTransform() => new JsonFileLoadTransform();

    public JsonFileSaveTransform CreateFileSaveTransform() => new JsonFileSaveTransform();

    public VariableResolveTransform CreateVariableResolveTransform(string prefix = "${", string suffix = "}")
        => new VariableResolveTransform(prefix, suffix);

    public ModelTransform<TSource, TTarget> CreateModelTransform<TSource, TTarget>(Func<TSource, TTarget> transform)
        => new ModelTransform<TSource, TTarget>(transform);


    public JsonObject MergeJson(JsonObject baseObj, JsonObject overrideObj, MergeOptions options = MergeOptions.ReplaceArray)
        => JsonMerger.Merge(baseObj, overrideObj, options);

    public T MergeModels<T>(T baseModel, T overrideModel) where T : class, new()
        => JsonMerger.MergeModels(baseModel, overrideModel);


    public async Task<DiagnosticResult> MeasureAsync<TInput, TOutput>(
        IPipeline<TInput, TOutput> pipeline, TInput input)
        => await JsonDiagnostics.MeasureAsync(pipeline, input);

    public JsonSizeInfo AnalyzeJson(string json)
        => JsonDiagnostics.AnalyzeJsonSize(json);


    public JsonNode? ToJsonNode<T>(T obj)
        => ObjectJsonExtensions.ToJsonNode(obj);

    public async Task<T?> LoadFromFileAsync<T>(string path, JsonSerializerOptions? options = null)
        => await _serializer.Value.DeserializeFromFileAsync<T>(path, options);

    public async Task SaveToFileAsync<T>(string path, T obj, JsonSerializerOptions? options = null)
        => await _serializer.Value.SerializeToFileAsync(path, obj, options);



    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_mediator.IsValueCreated)
                _mediator.Value.UnregisterAll();

            if (_eventBus.IsValueCreated)
                _eventBus.Value.Clear();

            if (_dataBinding.IsValueCreated)
                _dataBinding.Value.ClearAll();

            if (_containerManager.IsValueCreated)
                _containerManager.Value.ClearAll();

            if (_sessionManager.IsValueCreated)
                _sessionManager.Value.ClearAll();

            _logger.Value.LogInformation("UniversalMiddleware disposed");
        }
    }
}

internal class DelegateRenderPipeline<TTarget> : RenderPipelineBase<TTarget>
{
    private readonly Func<IRenderSession, Task>? _createAction;
    private readonly Func<IRenderSession, Task>? _layoutAction;
    private readonly Func<IRenderSession, Task>? _bindAction;
    private readonly Microsoft.Extensions.Logging.ILogger? _logger;

    public DelegateRenderPipeline(
        string name,
        Func<IRenderSession, Task>? createAction,
        Func<IRenderSession, Task>? layoutAction,
        Func<IRenderSession, Task>? bindAction,
        Microsoft.Extensions.Logging.ILogger? logger)
        : base(name)
    {
        _createAction = createAction;
        _layoutAction = layoutAction;
        _bindAction = bindAction;
        _logger = logger;
    }

    protected override Task ExecuteCreateStage(TTarget target, IRenderSession session)
    {
        if (_createAction != null)
            return _createAction(session);
        _logger?.LogDebug($"Create stage skipped for {typeof(TTarget).Name}");
        return Task.CompletedTask;
    }

    protected override Task ExecuteLayoutStage(TTarget target, IRenderSession session)
    {
        if (_layoutAction != null)
            return _layoutAction(session);
        _logger?.LogDebug($"Layout stage skipped for {typeof(TTarget).Name}");
        return Task.CompletedTask;
    }

    protected override Task ExecuteBindEventsStage(TTarget target, IRenderSession session)
    {
        if (_bindAction != null)
            return _bindAction(session);
        _logger?.LogDebug($"BindEvents stage skipped for {typeof(TTarget).Name}");
        return Task.CompletedTask;
    }
}

internal class DefaultConfigRenderPipeline<TContainer> : ConfigRenderPipeline<TContainer>
    where TContainer : IConfigContainer
{
    public DefaultConfigRenderPipeline(TConfig.IConfigLoader configLoader, string name = "ConfigRenderPipeline")
        : base(configLoader, name) { }

    protected override Task ExecuteCreateStage(TContainer target, IRenderSession session)
    {
        return Task.CompletedTask;
    }

    protected override Task ExecuteLayoutStage(TContainer target, IRenderSession session)
    {
        return Task.CompletedTask;
    }

    protected override Task ExecuteBindEventsStage(TContainer target, IRenderSession session)
    {
        return Task.CompletedTask;
    }
}
