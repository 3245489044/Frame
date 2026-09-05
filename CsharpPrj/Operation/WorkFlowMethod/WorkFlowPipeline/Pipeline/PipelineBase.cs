
using System.Collections.Concurrent;
using WorkflowExecution.Abstractions;
using WorkflowExecution.Pipeline;

namespace CommandFramework.Execution;

public abstract class PipelineBase<TContext>
{
    private readonly List<IPipelineStage> _fixedStages;
    private readonly ConcurrentDictionary<string, IPipelineStage> _dynamicStages = new(StringComparer.OrdinalIgnoreCase);
    private List<IPipelineStage>? _resolvedStages;
    private bool _dirty = true;
    private readonly object _buildLock = new();

    public string Name { get; }

    public IReadOnlyList<IPipelineStage> CurrentStages
    {
        get
        {
            EnsurePipelineBuilt();
            return _resolvedStages!.AsReadOnly();
        }
    }

    public int DynamicStageCount => _dynamicStages.Count;

    protected PipelineBase(string name, IEnumerable<IPipelineStage>? fixedStages = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _fixedStages = fixedStages?.ToList() ?? new List<IPipelineStage>();
    }


    public void RegisterStage(string name, IPipelineStage stage)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("阶段名称不能为空", nameof(name));
        ArgumentNullException.ThrowIfNull(stage);

        _dynamicStages[name] = stage;
        MarkDirty();
    }

    public bool RemoveStage(string name)
    {
        if (_dynamicStages.TryRemove(name, out _))
        {
            MarkDirty();
            return true;
        }
        return false;
    }

    public void ClearDynamicStages()
    {
        if (_dynamicStages.Count > 0)
        {
            _dynamicStages.Clear();
            MarkDirty();
        }
    }

    public IPipelineStage? GetStage(string name)
    {
        EnsurePipelineBuilt();
        return _resolvedStages?.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public bool InsertBefore(string targetName, IPipelineStage newStage, string? newName = null)
        => InsertRelative(targetName, newStage, newName, -0.5f);

    public bool InsertAfter(string targetName, IPipelineStage newStage, string? newName = null)
        => InsertRelative(targetName, newStage, newName, 0.5f);

    private bool InsertRelative(string targetName, IPipelineStage newStage, string? newName, float orderOffset)
    {
        ArgumentNullException.ThrowIfNull(newStage);

        var name = newName ?? newStage.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("阶段名称不能为空", nameof(name));

        EnsurePipelineBuilt();
        var target = _resolvedStages?.FirstOrDefault(s =>
            string.Equals(s.Name, targetName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            return false;
        }

        var stageWithOrder = new StageRunner(name, target.Order + orderOffset, newStage.ExecuteAsync);
        RegisterStage(name, stageWithOrder);
        return true;
    }


    public void MarkDirty()
    {
        _dirty = true;
    }

    public void RebuildPipeline()
    {
        lock (_buildLock)
        {
            RebuildPipelineInternal();
        }
    }

    private void RebuildPipelineInternal()
    {
        var merged = new List<IPipelineStage>(_fixedStages);
        merged.AddRange(_dynamicStages.Values);
        merged.Sort((a, b) => a.Order.CompareTo(b.Order));
        _resolvedStages = merged;
        _dirty = false;
    }

    protected void EnsurePipelineBuilt()
    {
        if (!_dirty && _resolvedStages != null)
            return;

        lock (_buildLock)
        {
            if (!_dirty && _resolvedStages != null)
                return;

            RebuildPipelineInternal();
        }
    }

    protected IReadOnlyList<IPipelineStage> GetResolvedStages()
    {
        EnsurePipelineBuilt();
        return _resolvedStages!;
    }
}
