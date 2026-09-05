
#nullable enable
using System.Diagnostics;
using TConfig.Abstractions;

namespace UniversalMiddleware.Rendering;

public abstract class RenderPipelineBase<TTarget> : IRenderPipeline
{
    private readonly string _name;
    private readonly List<IRenderStage> _stages = new();
    private readonly object _lock = new();
    private volatile bool _stagesInitialized;

    public string Name => _name;

    protected int StageCount
    {
        get { lock (_lock) { return _stages.Count; } }
    }

    public event EventHandler<RenderCompletedEventArgs>? RenderCompleted;

    protected RenderPipelineBase(string name = "RenderPipeline")
    {
        _name = name;
    }

    public void AddStage(IRenderStage stage)
    {
        lock (_lock)
        {
            _stages.Add(stage);
            _stages.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
    }

    private void EnsureStagesInitialized()
    {
        if (_stagesInitialized) return;
        lock (_lock)
        {
            if (_stagesInitialized) return;

            _stages.Add(new CreateStage(session => ExecuteCreateStage((TTarget)session.Target!, session)));
            _stages.Add(new LayoutStage(session => ExecuteLayoutStage((TTarget)session.Target!, session)));
            _stages.Add(new BindEventsStage(session => ExecuteBindEventsStage((TTarget)session.Target!, session)));

            _stages.Sort((a, b) => a.Order.CompareTo(b.Order));
            _stagesInitialized = true;
        }
    }

    public async Task RenderAsync(object target, object config)
    {
        var sw = Stopwatch.StartNew();
        var session = CreateSession(target, config);

        try
        {
            EnsureStagesInitialized();

            List<IRenderStage> stagesSnapshot;
            lock (_lock)
            {
                stagesSnapshot = new List<IRenderStage>(_stages);
            }

            foreach (var stage in stagesSnapshot)
            {
                await stage.ExecuteAsync(session);
            }

            sw.Stop();
            OnRenderCompleted(new RenderCompletedEventArgs(_name, true, sw.Elapsed));
        }
        catch (Exception ex)
        {
            sw.Stop();
            OnRenderCompleted(new RenderCompletedEventArgs(_name, false, sw.Elapsed, ex.Message));
            throw;
        }
    }

    public async Task ExecuteCreateStageOnlyAsync(object target, object config)
    {
        var session = CreateSession(target, config);
        EnsureStagesInitialized();
        await ExecuteCreateStage((TTarget)target, session);
    }

    public async Task ApplyLayoutAsync(object target)
    {
        var session = CreateSession(target, null);
        await ExecuteLayoutStage((TTarget)target, session);
    }

    public virtual async Task BindEventsAsync(object target)
    {
        var session = CreateSession(target, null);
        await ExecuteBindEventsStage((TTarget)target, session);
    }

    protected abstract Task ExecuteCreateStage(TTarget target, IRenderSession session);

    protected abstract Task ExecuteLayoutStage(TTarget target, IRenderSession session);

    protected abstract Task ExecuteBindEventsStage(TTarget target, IRenderSession session);

    protected virtual IRenderSession CreateSession(object target, object? config)
    {
        return new RenderSession(target, config);
    }

    protected virtual void OnRenderCompleted(RenderCompletedEventArgs args)
    {
        RenderCompleted?.Invoke(this, args);
    }


    async Task IRenderPipeline.ExecuteCreateStageAsync(object target, object config)
    {
        var session = CreateSession(target, config);
        EnsureStagesInitialized();
        await ExecuteCreateStage((TTarget)target, session);
    }

    async Task IRenderPipeline.ExecuteLayoutStageAsync(object target)
    {
        var session = CreateSession(target, null);
        await ExecuteLayoutStage((TTarget)target, session);
    }

    async Task IRenderPipeline.ExecuteBindEventsStageAsync(object target, object config)
    {
        var session = CreateSession(target, config);
        await ExecuteBindEventsStage((TTarget)target, session);
    }

    object? IRenderPipeline.GetActiveSession(object target)
    {
        return null;
    }

    void IRenderPipeline.DisposeSession(object target)
    {
    }
}
