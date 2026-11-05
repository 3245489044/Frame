
using System.Collections.Concurrent;
using BP = Modules.Pipeline;

namespace Modules.CentralPipeline;

public class PipelineStageRegistry<TContext>
{
    private readonly ConcurrentDictionary<PipelineStage, List<BP.IPipeline<TContext, TContext>>> _stagePipelines = new();
    private readonly object _lock = new();

    public event EventHandler<PipelineStageRegistryChangedEventArgs>? RegistryChanged;

    public IReadOnlyList<PipelineStage> RegisteredStages
    {
        get
        {
            lock (_lock)
            {
                return _stagePipelines.Keys.OrderBy(k => (int)k).ToList().AsReadOnly();
            }
        }
    }

    public int TotalCount
    {
        get
        {
            lock (_lock)
            {
                return _stagePipelines.Values.Sum(list => list.Count);
            }
        }
    }

    public void Register(PipelineStage stage, BP.IPipeline<TContext, TContext> pipeline)
    {
        if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));

        PipelineStageRegistryChangedEventArgs eventArgs;
        lock (_lock)
        {
            if (!_stagePipelines.ContainsKey(stage))
                _stagePipelines[stage] = new List<BP.IPipeline<TContext, TContext>>();

            _stagePipelines[stage].Add(pipeline);

            eventArgs = new PipelineStageRegistryChangedEventArgs
            {
                ChangeType = RegistryChangeType.Registered,
                Stage = stage,
                Pipeline = pipeline
            };
        }

        OnRegistryChanged(eventArgs);
    }

    public void InsertAt(PipelineStage stage, int index, BP.IPipeline<TContext, TContext> pipeline)
    {
        if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));

        PipelineStageRegistryChangedEventArgs eventArgs;
        lock (_lock)
        {
            if (!_stagePipelines.ContainsKey(stage))
                _stagePipelines[stage] = new List<BP.IPipeline<TContext, TContext>>();

            var list = _stagePipelines[stage];
            if (index > list.Count) index = list.Count;
            list.Insert(index, pipeline);

            eventArgs = new PipelineStageRegistryChangedEventArgs
            {
                ChangeType = RegistryChangeType.Inserted,
                Stage = stage,
                Pipeline = pipeline
            };
        }

        OnRegistryChanged(eventArgs);
    }

    public void Replace(PipelineStage stage, int index, BP.IPipeline<TContext, TContext> newPipeline)
    {
        if (newPipeline == null) throw new ArgumentNullException(nameof(newPipeline));
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));

        PipelineStageRegistryChangedEventArgs eventArgs;
        lock (_lock)
        {
            if (_stagePipelines.TryGetValue(stage, out var list) && index < list.Count)
            {
                var oldPipeline = list[index];
                list[index] = newPipeline;

                eventArgs = new PipelineStageRegistryChangedEventArgs
                {
                    ChangeType = RegistryChangeType.Replaced,
                    Stage = stage,
                    Pipeline = newPipeline,
                    OldPipeline = oldPipeline
                };
            }
            else
            {
                throw new InvalidOperationException(
                    $"阶段 {stage.GetDisplayName()} 中不存在索引 {index} 的 Pipeline");
            }
        }

        OnRegistryChanged(eventArgs);
    }

    public void ReplaceAll(PipelineStage stage, IEnumerable<BP.IPipeline<TContext, TContext>> pipelines)
    {
        if (pipelines == null) throw new ArgumentNullException(nameof(pipelines));

        PipelineStageRegistryChangedEventArgs eventArgs;
        lock (_lock)
        {
            var newList = pipelines.ToList();
            if (newList.Any(s => s == null))
                throw new ArgumentException("Pipeline 列表中不能包含 null 元素", nameof(pipelines));

            _stagePipelines[stage] = newList;

            eventArgs = new PipelineStageRegistryChangedEventArgs
            {
                ChangeType = RegistryChangeType.Replaced,
                Stage = stage
            };
        }

        OnRegistryChanged(eventArgs);
    }

    public void Remove(PipelineStage stage, int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));

        PipelineStageRegistryChangedEventArgs eventArgs;
        lock (_lock)
        {
            if (_stagePipelines.TryGetValue(stage, out var list) && index < list.Count)
            {
                var removedPipeline = list[index];
                list.RemoveAt(index);

                eventArgs = new PipelineStageRegistryChangedEventArgs
                {
                    ChangeType = RegistryChangeType.Removed,
                    Stage = stage,
                    Pipeline = removedPipeline
                };
            }
            else
            {
                throw new InvalidOperationException(
                    $"阶段 {stage.GetDisplayName()} 中不存在索引 {index} 的 Pipeline");
            }
        }

        OnRegistryChanged(eventArgs);
    }

    public void ClearStage(PipelineStage stage)
    {
        PipelineStageRegistryChangedEventArgs eventArgs;
        lock (_lock)
        {
            if (_stagePipelines.ContainsKey(stage))
            {
                _stagePipelines[stage] = new List<BP.IPipeline<TContext, TContext>>();
            }

            eventArgs = new PipelineStageRegistryChangedEventArgs
            {
                ChangeType = RegistryChangeType.Cleared,
                Stage = stage
            };
        }

        OnRegistryChanged(eventArgs);
    }

    public void ClearAll()
    {
        PipelineStageRegistryChangedEventArgs eventArgs;
        lock (_lock)
        {
            var stagesToClear = _stagePipelines.Keys.ToList();
            foreach (var stage in stagesToClear)
            {
                _stagePipelines[stage] = new List<BP.IPipeline<TContext, TContext>>();
            }

            eventArgs = new PipelineStageRegistryChangedEventArgs
            {
                ChangeType = RegistryChangeType.Cleared
            };
        }

        OnRegistryChanged(eventArgs);
    }

    public IReadOnlyList<BP.IPipeline<TContext, TContext>> GetPipelines(PipelineStage stage)
    {
        lock (_lock)
        {
            if (_stagePipelines.TryGetValue(stage, out var list))
            {
                return list.ToList().AsReadOnly();
            }
            return Array.Empty<BP.IPipeline<TContext, TContext>>();
        }
    }

    public Dictionary<PipelineStage, IReadOnlyList<BP.IPipeline<TContext, TContext>>> GetAllPipelines()
    {
        lock (_lock)
        {
            var result = new Dictionary<PipelineStage, IReadOnlyList<BP.IPipeline<TContext, TContext>>>();
            foreach (var stage in PipelineStageExtensions.GetAllStages())
            {
                if (_stagePipelines.TryGetValue(stage, out var list))
                {
                    result[stage] = list.ToList().AsReadOnly();
                }
                else
                {
                    result[stage] = Array.Empty<BP.IPipeline<TContext, TContext>>();
                }
            }
            return result;
        }
    }

    public int GetCount(PipelineStage stage)
    {
        lock (_lock)
        {
            if (_stagePipelines.TryGetValue(stage, out var list))
                return list.Count;
            return 0;
        }
    }

    protected virtual void OnRegistryChanged(PipelineStageRegistryChangedEventArgs e)
    {
        RegistryChanged?.Invoke(this, e);
    }
}

public enum RegistryChangeType
{
    Registered,

    Inserted,

    Replaced,

    Removed,

    Cleared
}

public class PipelineStageRegistryChangedEventArgs : EventArgs
{
    public RegistryChangeType ChangeType { get; set; }

    public PipelineStage? Stage { get; set; }

    public object? Pipeline { get; set; }

    public object? OldPipeline { get; set; }
}
