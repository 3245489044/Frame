
using System.Collections.Concurrent;
using Modules.InternalStorage.Core;
using TConfig.ContextFrame.Abstractions;

namespace Modules.InternalStorage.Initialization;

public class InitializationOrchestrator
{
    private readonly List<IInitializer> _initializers = new();
    private readonly IStorageEngine _engine;
    private bool _failFast = true;

    public bool FailFast
    {
        get => _failFast;
        set => _failFast = value;
    }

    public Dictionary<string, object?> ContextData { get; } = new();

    public InitializationOrchestrator(IStorageEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }


    public void Register(string name, Func<Task> initialize, string[]? dependsOn = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(initialize);

        _initializers.Add(new LambdaInitializer(name, initialize, dependsOn));
    }

    public void Register(string name, Action initialize, string[]? dependsOn = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(initialize);

        _initializers.Add(new LambdaInitializer(name, () =>
        {
            initialize();
            return Task.CompletedTask;
        }, dependsOn));
    }

    public void Register(string name, Func<IInitializationContext, Task> initialize, string[]? dependsOn = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(initialize);

        _initializers.Add(new LambdaInitializer(name, initialize, dependsOn));
    }

    public void Register(string name, Action<IInitializationContext> initialize, string[]? dependsOn = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(initialize);

        _initializers.Add(new LambdaInitializer(name, ctx =>
        {
            initialize(ctx);
            return Task.CompletedTask;
        }, dependsOn));
    }

    public void Register(IInitializer initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer);

        _initializers.Add(initializer);
    }


    public async Task<InitializationResult> ExecuteAsync(
        IStorageCell? storage = null,
        CancellationToken ct = default)
    {
        var sorted = TopologicalSort();

        var context = new InitializationContext(
            storage ?? _engine.RootCell,
            new StorageRegistry(),
            ct);

        foreach (var kvp in ContextData)
        {
            if (kvp.Key != null && kvp.Value != null)
            {
                context.Storage.Set(kvp.Key, kvp.Value, StorageLayer.Config);
            }
        }

        var results = new List<InitializerResult>();
        foreach (var initializer in sorted)
        {
            ct.ThrowIfCancellationRequested();

            var startTime = DateTime.UtcNow;
            try
            {
                await initializer.ExecuteAsync(context);
                var duration = DateTime.UtcNow - startTime;
                results.Add(new InitializerResult(initializer.Name, true, null, duration));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var duration = DateTime.UtcNow - startTime;
                results.Add(new InitializerResult(initializer.Name, false, ex, duration));

                if (_failFast)
                {
                    throw new InitializationException(
                        $"初始化器 '{initializer.Name}' 执行失败", ex);
                }
            }
        }

        return new InitializationResult(results);
    }


    public IReadOnlyList<IInitializer> TopologicalSort()
    {
        var nameMap = new Dictionary<string, IInitializer>(StringComparer.Ordinal);
        foreach (var initializer in _initializers)
        {
            if (!nameMap.TryAdd(initializer.Name, initializer))
            {
                throw new InvalidOperationException(
                    $"重复的初始化器名称: '{initializer.Name}'。每个初始化器必须有唯一的名称。");
            }
        }

        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var initializer in _initializers)
        {
            var name = initializer.Name;
            if (!adjacency.ContainsKey(name))
                adjacency[name] = new List<string>();
            if (!inDegree.ContainsKey(name))
                inDegree[name] = 0;

            foreach (var dep in initializer.DependsOn ?? Array.Empty<string>())
            {
                if (!nameMap.ContainsKey(dep))
                {
                    throw new InvalidOperationException(
                        $"初始化器 '{name}' 依赖的 '{dep}' 未注册。");
                }

                if (!adjacency.ContainsKey(dep))
                    adjacency[dep] = new List<string>();
                adjacency[dep].Add(name);

                if (!inDegree.ContainsKey(name))
                    inDegree[name] = 0;
                inDegree[name]++;
            }
        }

        var queue = new Queue<string>();
        foreach (var kvp in inDegree)
        {
            if (kvp.Value == 0)
                queue.Enqueue(kvp.Key);
        }

        var sortedNames = new List<string>(_initializers.Count);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            sortedNames.Add(name);

            if (adjacency.TryGetValue(name, out var dependents))
            {
                foreach (var dependent in dependents)
                {
                    inDegree[dependent]--;
                    if (inDegree[dependent] == 0)
                        queue.Enqueue(dependent);
                }
            }
        }

        if (sortedNames.Count < _initializers.Count)
        {
            var unprocessed = _initializers
                .Select(i => i.Name)
                .Except(sortedNames, StringComparer.Ordinal);
            var unprocessedList = string.Join(", ", unprocessed);
            throw new InvalidOperationException(
                $"检测到循环依赖，以下初始化器无法排序: [{unprocessedList}]。");
        }

        return sortedNames
            .Select(name => nameMap[name])
            .ToList()
            .AsReadOnly();
    }


    private sealed class LambdaInitializer : IInitializer
    {
        private readonly Func<IInitializationContext, Task> _initialize;

        public string Name { get; }
        public string[] DependsOn { get; }

        public LambdaInitializer(string name, Func<Task> initialize, string[]? dependsOn)
        {
            Name = name;
            _initialize = _ => initialize();
            DependsOn = dependsOn ?? Array.Empty<string>();
        }

        public LambdaInitializer(string name, Func<IInitializationContext, Task> initialize, string[]? dependsOn)
        {
            Name = name;
            _initialize = initialize;
            DependsOn = dependsOn ?? Array.Empty<string>();
        }

        public Task ExecuteAsync(IInitializationContext context)
        {
            return _initialize(context);
        }
    }
}

public class InitializationException : InvalidOperationException
{
    public InitializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
