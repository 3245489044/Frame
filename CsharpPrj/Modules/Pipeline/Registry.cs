
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkeletonFlow;

namespace Modules.Pipeline
{

    public interface IStrategy<in TInput, out TOutput>
    {
        string Name { get; }

        TOutput Execute(TInput input);
    }


    public class StrategyRegistry<TKey, TInput, TOutput>
        where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, IStrategy<TInput, TOutput>> _strategies = new();

        public void Register(TKey key, IStrategy<TInput, TOutput> strategy)
        {
            if (!_strategies.TryAdd(key, strategy))
                throw new InvalidOperationException($"Strategy for key '{key}' is already registered.");
        }

        public void RegisterOrUpdate(TKey key, IStrategy<TInput, TOutput> strategy)
            => _strategies[key] = strategy;

        public IStrategy<TInput, TOutput> Get(TKey key)
        {
            if (_strategies.TryGetValue(key, out var strategy))
                return strategy;
            throw new KeyNotFoundException($"No strategy found for key '{key}'.");
        }

        public bool TryGet(TKey key, out IStrategy<TInput, TOutput>? strategy)
            => _strategies.TryGetValue(key, out strategy);

        public bool ContainsKey(TKey key) => _strategies.ContainsKey(key);

        public bool Remove(TKey key) => _strategies.TryRemove(key, out _);

        public void Clear() => _strategies.Clear();

        public int Count => _strategies.Count;

        public IEnumerable<TKey> Keys => _strategies.Keys;

        public TOutput Execute(TKey key, TInput input)
            => Get(key).Execute(input);

        public bool TryExecute(TKey key, TInput input, out TOutput? result)
        {
            if (_strategies.TryGetValue(key, out var strategy))
            {
                result = strategy.Execute(input);
                return true;
            }
            result = default;
            return false;
        }
    }



    public class PipelineStageRegistry
    {
        private readonly ConcurrentDictionary<PipelineStage, IPipeline<object, object>> _stages = new();
        private IReadOnlyList<IPipeline<object, object>>? _cachedPipelines;
        private readonly object _cacheLock = new();

        public int Count => _stages.Count;

        public IEnumerable<PipelineStage> RegisteredStages => _stages.Keys;

        public void Register(PipelineStage stage, IPipeline<object, object> pipeline)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (!_stages.TryAdd(stage, pipeline))
                throw new InvalidOperationException($"Stage '{stage}' is already registered.");
            InvalidateCache();
        }

        public void RegisterOrUpdate(PipelineStage stage, IPipeline<object, object> pipeline)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            _stages[stage] = pipeline;
            InvalidateCache();
        }

        public IPipeline<object, object>? Get(PipelineStage stage)
            => _stages.TryGetValue(stage, out var pipeline) ? pipeline : null;

        public bool TryGet(PipelineStage stage, out IPipeline<object, object>? pipeline)
            => _stages.TryGetValue(stage, out pipeline);

        public bool Remove(PipelineStage stage)
        {
            var result = _stages.TryRemove(stage, out _);
            if (result) InvalidateCache();
            return result;
        }

        public void Clear()
        {
            _stages.Clear();
            InvalidateCache();
        }

        private void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedPipelines = null;
            }
        }

        public IReadOnlyList<IPipeline<object, object>> GetAllPipelines()
        {
            if (_cachedPipelines != null)
                return _cachedPipelines;

            lock (_cacheLock)
            {
                if (_cachedPipelines != null)
                    return _cachedPipelines;

                _cachedPipelines = PipelineStageExtensions.GetAllStages()
                    .Where(s => _stages.ContainsKey(s))
                    .Select(s => _stages[s])
                    .ToList()
                    .AsReadOnly();

                return _cachedPipelines;
            }
        }

        public IReadOnlyDictionary<PipelineStage, IPipeline<object, object>> GetAll()
            => new Dictionary<PipelineStage, IPipeline<object, object>>(_stages);

        public bool Contains(PipelineStage stage)
            => _stages.ContainsKey(stage);
    }
}
