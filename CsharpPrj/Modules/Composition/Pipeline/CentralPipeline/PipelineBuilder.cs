
using SkeletonFlow;
using BP = Modules.Pipeline;

namespace Modules.CentralPipeline;

public class PipelineBuilder<TContext> : IDisposable
{
    private readonly CentralPipeline<TContext> _pipeline;
    private readonly List<(PipelineStage Stage, ISkeleton<TContext, TContext> Skeleton)> _pendingRegistrations = new();
    private readonly List<(PipelineStage Stage, BP.IPipeline<TContext, TContext> Pipeline)> _pendingPipelineRegistrations = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private bool _disposed;

    private readonly Dictionary<Type, object> _globalServices = new();

    public PipelineBuilder(string? name = null)
    {
        _pipeline = new CentralPipeline<TContext>(name);
    }

    public PipelineBuilder(CentralPipeline<TContext> pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public PipelineBuilder<TContext> RegisterService<T>(T service) where T : class
    {
        if (service == null) return this;
        _lock.EnterWriteLock();
        try
        {
            _globalServices[typeof(T)] = service;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        return this;
    }

    public PipelineBuilder<TContext> Register(PipelineStage stage, ISkeleton<TContext, TContext> skeleton)
    {
        if (skeleton == null) throw new ArgumentNullException(nameof(skeleton));
        _lock.EnterWriteLock();
        try
        {
            _pendingRegistrations.Add((stage, skeleton));
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        return this;
    }

    public PipelineBuilder<TContext> RegisterPipeline(PipelineStage stage, BP.IPipeline<TContext, TContext> pipeline)
    {
        if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
        _lock.EnterWriteLock();
        try
        {
            _pendingPipelineRegistrations.Add((stage, pipeline));
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        return this;
    }

    public PipelineBuilder<TContext> RegisterAll(PipelineStage stage, params ISkeleton<TContext, TContext>[] skeletons)
    {
        if (skeletons == null) throw new ArgumentNullException(nameof(skeletons));
        _lock.EnterWriteLock();
        try
        {
            foreach (var skeleton in skeletons)
            {
                _pendingRegistrations.Add((stage, skeleton));
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        return this;
    }

    public PipelineBuilder<TContext> AddInitialization(ISkeleton<TContext, TContext> skeleton)
        => Register(PipelineStage.Initialization, skeleton);

    public PipelineBuilder<TContext> AddInitializationPipeline(BP.IPipeline<TContext, TContext> pipeline)
        => RegisterPipeline(PipelineStage.Initialization, pipeline);

    public PipelineBuilder<TContext> AddPreProcessing(ISkeleton<TContext, TContext> skeleton)
        => Register(PipelineStage.PreProcessing, skeleton);

    public PipelineBuilder<TContext> AddPreProcessingPipeline(BP.IPipeline<TContext, TContext> pipeline)
        => RegisterPipeline(PipelineStage.PreProcessing, pipeline);

    public PipelineBuilder<TContext> AddExecution(ISkeleton<TContext, TContext> skeleton)
        => Register(PipelineStage.Execution, skeleton);

    public PipelineBuilder<TContext> AddExecutionPipeline(BP.IPipeline<TContext, TContext> pipeline)
        => RegisterPipeline(PipelineStage.Execution, pipeline);

    public PipelineBuilder<TContext> AddPostProcessing(ISkeleton<TContext, TContext> skeleton)
        => Register(PipelineStage.PostProcessing, skeleton);

    public PipelineBuilder<TContext> AddPostProcessingPipeline(BP.IPipeline<TContext, TContext> pipeline)
        => RegisterPipeline(PipelineStage.PostProcessing, pipeline);

    public PipelineBuilder<TContext> AddFinalization(ISkeleton<TContext, TContext> skeleton)
        => Register(PipelineStage.Finalization, skeleton);

    public PipelineBuilder<TContext> AddFinalizationPipeline(BP.IPipeline<TContext, TContext> pipeline)
        => RegisterPipeline(PipelineStage.Finalization, pipeline);

    public CentralPipeline<TContext> Build()
    {
        _lock.EnterWriteLock();
        try
        {
            if (_globalServices.Count > 0)
            {
                _pipeline.SetGlobalServices(new Dictionary<Type, object>(_globalServices));
            }

            foreach (var (stage, skeleton) in _pendingRegistrations)
            {
                _pipeline.Register(stage, skeleton);
            }
            _pendingRegistrations.Clear();

            foreach (var (stage, pipeline) in _pendingPipelineRegistrations)
            {
                _pipeline.RegisterPipeline(stage, pipeline);
            }
            _pendingPipelineRegistrations.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        _pipeline.Build();

        return _pipeline;
    }

    public CentralPipeline<TContext> Pipeline => _pipeline;


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock?.Dispose();
    }
}

public static class PipelineBuilderExtensions
{
    public static PipelineBuilder<TContext> AddForEach<TContext, TItem>(
        this PipelineBuilder<TContext> builder,
        PipelineStage stage,
        Func<TContext, IReadOnlyList<TItem>> itemsSelector,
        BP.IPipeline<TContext, TContext> itemPipeline,
        Action<TContext, TItem, int>? onItemBefore = null,
        Action<TContext, TItem, int, bool>? onItemAfter = null,
        int? maxCount = null,
        string? name = null)
    {
        var forEach = new BP.ForEachPipeline<TContext, TItem>(
            itemsSelector, itemPipeline, onItemBefore, onItemAfter, maxCount, name: name);
        return builder.RegisterPipeline(stage, forEach);
    }

    public static PipelineBuilder<TContext> AddSwitch<TContext, TKey>(
        this PipelineBuilder<TContext> builder,
        PipelineStage stage,
        Func<TContext, TKey> keySelector,
        Dictionary<TKey, BP.IPipeline<TContext, TContext>> branches,
        BP.IPipeline<TContext, TContext>? defaultBranch = null,
        string? name = null)
        where TKey : notnull
    {
        var switchPipeline = new BP.PipelineSwitch<TContext, TKey>(
            keySelector, branches, defaultBranch, name);
        return builder.RegisterPipeline(stage, switchPipeline);
    }

    public static PipelineBuilder<TContext> AddBranch<TContext>(
        this PipelineBuilder<TContext> builder,
        PipelineStage stage,
        Func<TContext, bool> predicate,
        BP.IPipeline<TContext, TContext> trueBranch,
        BP.IPipeline<TContext, TContext> falseBranch,
        string? name = null)
    {
        var branch = new BP.PipelineBranch<TContext>(
            predicate, trueBranch, falseBranch, name);
        return builder.RegisterPipeline(stage, branch);
    }

    public static PipelineBuilder<TContext> AddTryCatch<TContext>(
        this PipelineBuilder<TContext> builder,
        PipelineStage stage,
        BP.IPipeline<TContext, TContext> mainPipeline,
        BP.IPipeline<TContext, TContext>? fallbackPipeline = null,
        Func<Exception, bool>? exceptionFilter = null,
        Action<Exception>? onError = null,
        string? name = null)
    {
        var tryCatch = new BP.PipelineTryCatch<TContext>(
            mainPipeline, fallbackPipeline, exceptionFilter, onError, name);
        return builder.RegisterPipeline(stage, tryCatch);
    }

    public static PipelineBuilder<TContext> AddRetry<TContext>(
        this PipelineBuilder<TContext> builder,
        PipelineStage stage,
        BP.IPipeline<TContext, TContext> innerPipeline,
        int maxRetries = 3,
        int delayMs = 100,
        Func<Exception, int, bool>? shouldRetry = null,
        Action<Exception, int>? onRetry = null,
        string? name = null)
    {
        var retry = new BP.PipelineRetry<TContext>(
            innerPipeline, maxRetries, delayMs, shouldRetry, onRetry, name);
        return builder.RegisterPipeline(stage, retry);
    }
}
