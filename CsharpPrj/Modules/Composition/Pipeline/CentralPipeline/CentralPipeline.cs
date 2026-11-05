
using SkeletonFlow;
using BP = Modules.Pipeline;

namespace Modules.CentralPipeline;

public class CentralPipeline<TContext> : BP.PipelineBase<TContext, TContext>, IDisposable
{
    private readonly PipelineStageRegistry<TContext> _registry;
    private BP.IPipeline<TContext, TContext>? _pipeline;
    private BP.PipelineStatus _status = BP.PipelineStatus.Idle;
    private readonly object _statusLock = new();
    private bool _disposed;
    private volatile bool _pipelineDirty = true;

    private IReadOnlyDictionary<Type, object>? _globalServices;

    public PipelineStageRegistry<TContext> Registry => _registry;

    public BP.PipelineStatus Status
    {
        get { lock (_statusLock) return _status; }
        private set { lock (_statusLock) _status = value; }
    }

    public CentralPipeline(string? name = null)
        : base(name ?? $"Pipeline-{Guid.NewGuid():N}", "CentralPipeline execution engine")
    {
        _registry = new PipelineStageRegistry<TContext>();
        _registry.RegistryChanged += OnRegistryChanged;
    }

    public CentralPipeline(PipelineStageRegistry<TContext> registry, string? name = null)
        : base(name ?? $"Pipeline-{Guid.NewGuid():N}", "CentralPipeline execution engine")
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _registry.RegistryChanged += OnRegistryChanged;
    }

    public CentralPipeline(string? name, IReadOnlyDictionary<Type, object>? globalServices)
        : base(name ?? $"Pipeline-{Guid.NewGuid():N}", "CentralPipeline execution engine")
    {
        _registry = new PipelineStageRegistry<TContext>();
        _registry.RegistryChanged += OnRegistryChanged;
        _globalServices = globalServices;
    }

    public void SetGlobalServices(IReadOnlyDictionary<Type, object> services)
    {
        _globalServices = services;
    }

    private void OnRegistryChanged(object? sender, PipelineStageRegistryChangedEventArgs e)
    {
        _pipelineDirty = true;
    }

    public void Register(PipelineStage stage, ISkeleton<TContext, TContext> skeleton)
    {
        if (skeleton == null) throw new ArgumentNullException(nameof(skeleton));
        var adapter = new SkeletonToPipelineAdapter<TContext>(skeleton);
        _registry.Register(stage, adapter);
    }

    public void RegisterPipeline(PipelineStage stage, BP.IPipeline<TContext, TContext> pipeline)
    {
        _registry.Register(stage, pipeline);
    }

    public void Build()
    {
        if (!_pipelineDirty && _pipeline != null)
            return;

        lock (_statusLock)
        {
            if (!_pipelineDirty && _pipeline != null)
                return;

            var allPipelines = new List<BP.IPipeline<TContext, TContext>>();

            foreach (var stage in PipelineStageExtensions.GetAllStages())
            {
                var stagePipelines = _registry.GetPipelines(stage);
                allPipelines.AddRange(stagePipelines);
            }

            if (_globalServices != null)
            {
                foreach (var pipeline in allPipelines)
                {
                    if (pipeline is SkeletonToPipelineAdapter<TContext> adapter)
                    {
                        adapter.SetGlobalServices(_globalServices);
                    }
                }
            }

            if (allPipelines.Count == 0)
            {
                _pipeline = new BP.DelegatePipeline<TContext, TContext>(
                    "EmptyPipeline",
                    syncWithCtx: (ctx, _) => ctx,
                    asyncWithCtx: (ctx, _) => Task.FromResult(ctx),
                    description: "空管线（未注册任何 Skeleton）");
            }
            else
            {
                _pipeline = allPipelines[0];
                for (int i = 1; i < allPipelines.Count; i++)
                {
                    _pipeline = BP.PipelineExtensions.Then(_pipeline, allPipelines[i]);
                }
            }

            _pipelineDirty = false;
            Status = BP.PipelineStatus.Completed;
        }
    }

    protected override TContext ExecuteCore(TContext input, BP.PipelineContext context)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));

        Build();

        if (Status == BP.PipelineStatus.Cancelled)
            throw new InvalidOperationException("管线已取消，无法执行。");

        try
        {
            Status = BP.PipelineStatus.Running;

            context.CancellationToken.ThrowIfCancellationRequested();

            var output = _pipeline!.Execute(input, context);
            Status = BP.PipelineStatus.Completed;
            return output;
        }
        catch (OperationCanceledException)
        {
            Status = BP.PipelineStatus.Cancelled;
            throw;
        }
        catch (Exception)
        {
            Status = BP.PipelineStatus.Failed;
            throw;
        }
    }

    protected override async Task<TContext> ExecuteCoreAsync(TContext input, BP.PipelineContext context)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));

        Build();

        if (Status == BP.PipelineStatus.Cancelled)
            throw new InvalidOperationException("管线已取消，无法执行。");

        try
        {
            Status = BP.PipelineStatus.Running;

            context.CancellationToken.ThrowIfCancellationRequested();

            var output = await _pipeline!.ExecuteAsync(input, context).ConfigureAwait(false);
            Status = BP.PipelineStatus.Completed;
            return output;
        }
        catch (OperationCanceledException)
        {
            Status = BP.PipelineStatus.Cancelled;
            throw;
        }
        catch (Exception)
        {
            Status = BP.PipelineStatus.Failed;
            throw;
        }
    }

    public void Rebuild()
    {
        _pipelineDirty = true;
        Build();
    }

    public void Cancel()
    {
        Status = BP.PipelineStatus.Cancelled;
    }

    public string GetPipelineDescription()
    {
        Build();

        var descriptions = new List<string>();
        foreach (var stage in PipelineStageExtensions.GetAllStages())
        {
            var pipelines = _registry.GetPipelines(stage);
            if (pipelines.Count > 0)
            {
                var stageName = stage.GetDisplayName();
                var pipelineNames = pipelines.Select(p => p.GetType().Name);
                descriptions.Add($"{stageName}: {string.Join(", ", pipelineNames)}");
            }
        }

        return descriptions.Count > 0
            ? $"Pipeline [{Name}]:\n  " + string.Join("\n  ", descriptions)
            : $"Pipeline [{Name}]: (空)";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registry.RegistryChanged -= OnRegistryChanged;
        _pipeline = null;
        Status = BP.PipelineStatus.Cancelled;
    }

    public override string ToString()
    {
        return $"CentralPipeline [Name={Name}, Status={Status}, Skeletons={_registry.TotalCount}]";
    }
}
