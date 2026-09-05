using CSG.Unified.Engine;
using CSG.Unified.Rules;
using CSG.Unified.Validation;

namespace CSG.Unified;

public class CsgFacade : IDisposable
{

    private readonly ITransformEngine _engine;

    private readonly IAdapterRegistry _registry;

    private readonly PlanOptimizer _optimizer;

    private readonly PlanCache? _cache;

    private readonly IPlanExecutor _executor;

    private readonly ValidationEngine _validationEngine;

    private readonly ConsistencyValidator _consistencyValidator;

    private readonly CsgFacadeOptions _options;

    private bool _disposed;


    public CsgFacade()
        : this(_ => { })
    {
    }

    public CsgFacade(Action<CsgFacadeOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        _options = new CsgFacadeOptions();
        configureOptions(_options);

        _engine = _options.TransformEngine ?? new TransformEngine();

        _registry = _options.AdapterRegistry ?? new AdapterRegistry();

        _optimizer = new PlanOptimizer(_engine)
            .WithAdapterRegistry(_registry);

        if (_options.UseDefaultRules)
        {
            RegisterDefaultRules();
        }

        if (_options.CustomRules is { Count: > 0 })
        {
            _optimizer.WithRules(_options.CustomRules);
        }

        if (_options.EnableCache)
        {
            _cache = new PlanCache(_optimizer);
        }

        _executor = new PlanExecutor();

        _validationEngine = new ValidationEngine();
        _consistencyValidator = new ConsistencyValidator();

        if (_options.AutoRegisterBuiltInAdapters)
        {
            RegisterBuiltInAdapters();
        }
    }


    public CanonicalForm ToCanonical(IConfiguration config)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(config);
        return _engine.ConfigToCanonical(config);
    }

    public TConfig FromCanonical<TConfig>(CanonicalForm form)
        where TConfig : IConfiguration, new()
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(form);
        return _engine.CanonicalToConfig<TConfig>(form);
    }

    public CanonicalForm ToCanonical(IStateMachine stateMachine)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(stateMachine);
        return _engine.StateMachineToCanonical(stateMachine);
    }

    public IStateMachine ToStateMachine(CanonicalForm form)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(form);
        return _engine.CanonicalToStateMachine(form);
    }

    public CanonicalForm ToCanonical(IGraph graph)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(graph);
        return _engine.GraphToCanonical(graph);
    }

    public IGraph ToGraph(CanonicalForm form)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(form);
        return _engine.CanonicalToGraph(form);
    }


    public void RegisterAdapter<TSource, TTarget>(Func<TSource, TTarget> adapter, AdapterCapabilities? capabilities = null)
        where TSource : class
        where TTarget : class
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(adapter);

        if (_registry is AdapterRegistry concreteRegistry)
        {
            concreteRegistry.Register(adapter, capabilities);
        }
        else
        {
            if (capabilities != null)
            {
                _registry.Register(capabilities);
            }
        }
    }

    public void RegisterCapabilities(AdapterCapabilities capabilities)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(capabilities);
        _registry.Register(capabilities);
    }

    public bool HasAdapter<TSource, TTarget>()
        where TSource : class
        where TTarget : class
    {
        ThrowIfDisposed();

        if (_registry is AdapterRegistry concreteRegistry)
        {
            return concreteRegistry.HasAdapter<TSource, TTarget>();
        }

        return false;
    }

    public AdapterCapabilities? GetCapabilities<TSource, TTarget>()
        where TSource : class
        where TTarget : class
    {
        ThrowIfDisposed();

        if (_registry is AdapterRegistry concreteRegistry)
        {
            return concreteRegistry.GetCapabilities<TSource, TTarget>();
        }

        return null;
    }

    public List<AdapterCapabilities> FindAdapterByOperation(string operation)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        if (_registry is AdapterRegistry concreteRegistry)
        {
            return concreteRegistry.FindByOperation(operation);
        }

        var cap = _registry.GetCapabilities(operation);
        return cap != null ? new List<AdapterCapabilities> { cap } : new List<AdapterCapabilities>();
    }

    public AdapterRegistryInfo GetAdapterInfo()
    {
        ThrowIfDisposed();

        if (_registry is AdapterRegistry concreteRegistry)
        {
            return concreteRegistry.GetInfo();
        }

        return new AdapterRegistryInfo();
    }

    public List<AdapterCapabilities> ListAllCapabilities()
    {
        ThrowIfDisposed();
        return _registry.GetAllCapabilities().ToList();
    }


    public Dictionary<string, object?> Execute(Action<TransformBatch> configureBatch)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(configureBatch);

        var batch = new TransformBatch(_engine);
        configureBatch(batch);

        return ExecuteBatch(batch);
    }

    public Dictionary<string, object?> Execute(TransformBatch batch)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(batch);
        return ExecuteBatch(batch);
    }

    public T? ExecuteAndGet<T>(Action<TransformBatch> configureBatch, string stepName)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(configureBatch);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepName);

        var results = Execute(configureBatch);

        if (results.TryGetValue(stepName, out var result) && result is T typedResult)
            return typedResult;

        return default;
    }

    private Dictionary<string, object?> ExecuteBatch(TransformBatch batch)
    {
        if (_cache != null)
        {
            var plan = _cache.GetOrOptimize(batch);
            return _executor.Execute(plan, _engine);
        }

        return batch.Execute();
    }


    public ValidationResult Validate(CanonicalForm form)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(form);
        return _validationEngine.Validate(form);
    }

    public ValidationResult ValidateConsistency(
        CanonicalForm? configForm,
        CanonicalForm? stateMachineForm,
        CanonicalForm? graphForm)
    {
        ThrowIfDisposed();

        configForm ??= new CanonicalForm();
        stateMachineForm ??= new CanonicalForm();
        graphForm ??= new CanonicalForm();

        return _consistencyValidator.Validate(configForm, stateMachineForm, graphForm);
    }

    public ValidationResult ValidateConsistency(IConfiguration config, IGraph graph)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(graph);

        var cForm = _engine.ConfigToCanonical(config);
        var gForm = _engine.GraphToCanonical(graph);

        return _consistencyValidator.Validate(cForm, new CanonicalForm(), gForm);
    }

    public ValidationResult ValidateConsistency(IConfiguration config, IStateMachine stateMachine)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(stateMachine);

        var cForm = _engine.ConfigToCanonical(config);
        var sForm = _engine.StateMachineToCanonical(stateMachine);

        return _consistencyValidator.Validate(cForm, sForm, new CanonicalForm());
    }


    public List<TransformOperation> ListOperations()
    {
        ThrowIfDisposed();
        return _engine.ListOperations();
    }

    public List<TransformOperation> ListOperationsByCategory(string category)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        return _engine.ListOperationsByCategory(category);
    }

    public object CallOperation(string operationName, object input)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(input);
        return _engine.CallOperation(operationName, input);
    }


    public void ClearCache()
    {
        ThrowIfDisposed();
        _cache?.Clear();
    }

    public int CacheCount => _cache?.Count ?? 0;

    public void InvalidateCache(ITransformBatch batch)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(batch);
        _cache?.Invalidate(batch);
    }


    public void WithRule(IOptimizationRule rule)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rule);
        _optimizer.WithRule(rule);
    }

    public void WithRules(IEnumerable<IOptimizationRule> rules)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rules);
        _optimizer.WithRules(rules);
    }

    public string GetOptimizerInfo()
    {
        ThrowIfDisposed();
        return _optimizer.GetInfo();
    }


    public static string GetProtocolInfo() => CsgProtocol.GetInfo();

    public static Version CurrentProtocolVersion => CsgProtocol.CurrentVersion;


    public void Reset()
    {
        ThrowIfDisposed();
        _cache?.Clear();

        if (_registry is AdapterRegistry concreteRegistry)
        {
            concreteRegistry.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cache?.Clear();

        if (_registry is IDisposable disposableRegistry)
        {
            disposableRegistry.Dispose();
        }

        GC.SuppressFinalize(this);
    }


    private void RegisterDefaultRules()
    {
        _optimizer
            .WithRule(new DependencyAnalysisRule())
            .WithRule(new ParallelMarkRule())
            .WithRule(new SequentialMergeRule())
            .WithRule(new DeadCodeEliminationRule())
            .WithRule(new ConstantFoldRule())
            .WithRule(new DomainValidationRule());
    }

    private void RegisterBuiltInAdapters()
    {
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CsgFacade));
        }
    }


    public ITransformEngine Engine => _engine;

    public IAdapterRegistry Registry => _registry;

    public PlanOptimizer Optimizer => _optimizer;

    public bool IsDisposed => _disposed;
}
