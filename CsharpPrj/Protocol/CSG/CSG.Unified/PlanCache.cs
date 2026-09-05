using System.Security.Cryptography;
using System.Text;
using CSG.Unified;

namespace CSG.Unified;

public class PlanCache
{
    private readonly Dictionary<string, ExecutionPlan> _cache = new();
    private readonly PlanOptimizer _optimizer;
    private readonly object _lock = new();

    public PlanCache(PlanOptimizer optimizer)
    {
        _optimizer = optimizer ?? throw new ArgumentNullException(nameof(optimizer));
    }

    public ExecutionPlan GetOrOptimize(ITransformBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var hash = ComputeHash(batch);

        lock (_lock)
        {
            if (_cache.TryGetValue(hash, out var cached))
            {
                return cached;
            }
        }

        var plan = _optimizer.Optimize(batch);

        lock (_lock)
        {
            _cache[hash] = plan;
        }

        return plan;
    }

    public bool TryGet(ITransformBatch batch, out ExecutionPlan? plan)
    {
        var hash = ComputeHash(batch);

        lock (_lock)
        {
            return _cache.TryGetValue(hash, out plan);
        }
    }

    public void Add(ITransformBatch batch, ExecutionPlan plan)
    {
        var hash = ComputeHash(batch);

        lock (_lock)
        {
            _cache[hash] = plan;
        }
    }

    public void Invalidate(ITransformBatch batch)
    {
        var hash = ComputeHash(batch);

        lock (_lock)
        {
            _cache.Remove(hash);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
    }

    public int Count
    {
        get { lock (_lock) { return _cache.Count; } }
    }

    private static string ComputeHash(ITransformBatch batch)
    {
        var steps = batch.GetStepData();

        var hashBuilder = new StringBuilder();
        foreach (var step in steps)
        {
            hashBuilder.Append(step.StepName);
            hashBuilder.Append('|');
            hashBuilder.Append(step.OperationName);
            hashBuilder.Append('|');
            hashBuilder.Append(step.IsReference ? "ref" : (step.Input?.GetType().FullName ?? "null"));
            hashBuilder.Append('|');
            hashBuilder.Append(step.IsReference && step.Input is string refName ? refName : "direct");
            hashBuilder.Append(';');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashBuilder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
