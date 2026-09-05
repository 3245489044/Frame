using CSG.Unified;

namespace CSG.Unified;

public class AdapterRegistry : IAdapterRegistry
{

    private class AdapterStore<TTarget>
    {
        public Dictionary<string, Func<object, TTarget>> Adapters { get; } = new();
        public Dictionary<string, AdapterCapabilities> Capabilities { get; } = new();
    }

    private readonly AdapterStore<IConfiguration> _configStore = new();
    private readonly AdapterStore<IStateMachine> _smStore = new();
    private readonly AdapterStore<IGraph> _graphStore = new();


    public void Register<TSource, TTarget>(Func<TSource, TTarget> adapter, AdapterCapabilities? capabilities = null)
        where TSource : class
        where TTarget : class
    {
        var store = GetStore<TTarget>();
        var key = typeof(TSource).FullName ?? typeof(TSource).Name;
        store.Adapters[key] = obj => adapter((TSource)obj);
        if (capabilities != null)
            store.Capabilities[key] = capabilities;
    }

    public TTarget Adapt<TTarget>(object obj)
        where TTarget : class
    {
        var store = GetStore<TTarget>();
        return AdaptFromStore(store, obj);
    }

    public bool HasAdapter<TSource, TTarget>()
        where TSource : class
        where TTarget : class
    {
        var store = GetStore<TTarget>();
        var typeName = typeof(TSource).FullName ?? typeof(TSource).Name;
        return store.Adapters.ContainsKey(typeName);
    }

    public AdapterCapabilities? GetCapabilities<TSource, TTarget>()
        where TSource : class
        where TTarget : class
    {
        var store = GetStore<TTarget>();
        var typeName = typeof(TSource).FullName ?? typeof(TSource).Name;
        return store.Capabilities.GetValueOrDefault(typeName);
    }


    private AdapterStore<TTarget> GetStore<TTarget>()
        where TTarget : class
    {
        if (typeof(TTarget) == typeof(IConfiguration))
            return (AdapterStore<TTarget>)(object)_configStore;
        if (typeof(TTarget) == typeof(IStateMachine))
            return (AdapterStore<TTarget>)(object)_smStore;
        if (typeof(TTarget) == typeof(IGraph))
            return (AdapterStore<TTarget>)(object)_graphStore;
        throw new NotSupportedException($"不支持的目标接口类型: {typeof(TTarget).Name}");
    }


    private static TTarget AdaptFromStore<TTarget>(AdapterStore<TTarget> store, object obj)
        where TTarget : class
    {
        var typeName = obj.GetType().FullName ?? obj.GetType().Name;
        if (store.Adapters.TryGetValue(typeName, out var adapter))
            return adapter(obj);
        throw new NotSupportedException($"No adapter registered for '{typeName}' to '{typeof(TTarget).Name}'");
    }


    public TTarget AdaptByType<TTarget>(object obj)
        where TTarget : class
    {
        var typeName = obj.GetType().FullName ?? obj.GetType().Name;

        if (typeof(TTarget) == typeof(IConfiguration))
        {
            if (_configStore.Adapters.TryGetValue(typeName, out var configAdapter))
                return (TTarget)configAdapter(obj);
        }
        else if (typeof(TTarget) == typeof(IStateMachine))
        {
            if (_smStore.Adapters.TryGetValue(typeName, out var smAdapter))
                return (TTarget)smAdapter(obj);
        }
        else if (typeof(TTarget) == typeof(IGraph))
        {
            if (_graphStore.Adapters.TryGetValue(typeName, out var graphAdapter))
                return (TTarget)graphAdapter(obj);
        }

        throw new NotSupportedException(
            $"No adapter registered for '{typeName}' to '{typeof(TTarget).Name}'");
    }

    public void Clear()
    {
        _configStore.Adapters.Clear();
        _configStore.Capabilities.Clear();
        _smStore.Adapters.Clear();
        _smStore.Capabilities.Clear();
        _graphStore.Adapters.Clear();
        _graphStore.Capabilities.Clear();
    }

    public AdapterRegistryInfo GetInfo()
    {
        return new AdapterRegistryInfo
        {
            ConfigAdapters = _configStore.Adapters.Keys.ToList(),
            StateMachineAdapters = _smStore.Adapters.Keys.ToList(),
            GraphAdapters = _graphStore.Adapters.Keys.ToList(),
            ConfigCapabilities = _configStore.Capabilities.Values.ToList(),
            StateMachineCapabilities = _smStore.Capabilities.Values.ToList(),
            GraphCapabilities = _graphStore.Capabilities.Values.ToList()
        };
    }


    public List<AdapterCapabilities> ListAllCapabilities()
    {
        var all = new List<AdapterCapabilities>();
        all.AddRange(_configStore.Capabilities.Values);
        all.AddRange(_smStore.Capabilities.Values);
        all.AddRange(_graphStore.Capabilities.Values);
        return all;
    }

    public List<AdapterCapabilities> FindByOperation(string operation)
    {
        return ListAllCapabilities()
            .Where(c => c.SupportedOperations.Contains(operation, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    public List<AdapterCapabilities> FindBySource(string source)
    {
        return ListAllCapabilities()
            .Where(c => c.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }


    void IAdapterRegistry.Register(AdapterCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        if (capabilities.SupportedFormTypes.Contains("Configuration", StringComparer.OrdinalIgnoreCase))
        {
            _configStore.Capabilities[capabilities.Source] = capabilities;
        }
        if (capabilities.SupportedFormTypes.Contains("StateMachine", StringComparer.OrdinalIgnoreCase))
        {
            _smStore.Capabilities[capabilities.Source] = capabilities;
        }
        if (capabilities.SupportedFormTypes.Contains("Graph", StringComparer.OrdinalIgnoreCase))
        {
            _graphStore.Capabilities[capabilities.Source] = capabilities;
        }
    }

    AdapterCapabilities? IAdapterRegistry.GetCapabilities(string operationName)
    {
        foreach (var cap in _configStore.Capabilities.Values)
        {
            if (cap.SupportedOperations.Contains(operationName, StringComparer.OrdinalIgnoreCase))
                return cap;
        }
        foreach (var cap in _smStore.Capabilities.Values)
        {
            if (cap.SupportedOperations.Contains(operationName, StringComparer.OrdinalIgnoreCase))
                return cap;
        }
        foreach (var cap in _graphStore.Capabilities.Values)
        {
            if (cap.SupportedOperations.Contains(operationName, StringComparer.OrdinalIgnoreCase))
                return cap;
        }
        return null;
    }

    IReadOnlyList<AdapterCapabilities> IAdapterRegistry.GetAllCapabilities()
    {
        return ListAllCapabilities().AsReadOnly();
    }
}

public class AdapterRegistryInfo
{
    public List<string> ConfigAdapters { get; init; } = new();

    public List<string> StateMachineAdapters { get; init; } = new();

    public List<string> GraphAdapters { get; init; } = new();

    public List<AdapterCapabilities> ConfigCapabilities { get; init; } = new();

    public List<AdapterCapabilities> StateMachineCapabilities { get; init; } = new();

    public List<AdapterCapabilities> GraphCapabilities { get; init; } = new();

    public int TotalCount => ConfigAdapters.Count + StateMachineAdapters.Count + GraphAdapters.Count;
}
