
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using TConfig;
using Microsoft.Extensions.DependencyInjection;

namespace Modules.Registry;


public class DefaultConflictResolver : IConflictResolver
{
    public ConflictStrategy Resolve(string key, string sourceA, string sourceB)
        => ConflictStrategy.Throw;
}


public class ConfigurableConflictResolver : IConflictResolver
{
    private readonly ConflictStrategy _defaultStrategy;
    private readonly Dictionary<string, ConflictStrategy> _overrides;

    public ConfigurableConflictResolver(
        ConflictStrategy defaultStrategy = ConflictStrategy.Overwrite,
        Dictionary<string, ConflictStrategy>? overrides = null)
    {
        _defaultStrategy = defaultStrategy;
        _overrides = overrides ?? new Dictionary<string, ConflictStrategy>(StringComparer.OrdinalIgnoreCase);
    }

    public ConflictStrategy Resolve(string key, string sourceA, string sourceB)
        => _overrides.TryGetValue(key, out var strategy) ? strategy : _defaultStrategy;
}


public class UnifiedRegistryCenter : IRegistryManager, IRegistryQuery, IRegistryDiagnostics, IRegistryLifecycle
{

    public static UnifiedRegistryCenter Instance { get; } = new UnifiedRegistryCenter();


    public FunctionRegistry Functions { get; }

    public IWorkflowRegistry Workflows { get; private set; }

    public IFunctionExecutor Executor { get; private set; }

    public IPipelineExecutor Pipeline { get; private set; }


    public Func<FunctionRegistry, IWorkflowRegistry>? WorkflowFactory { get; set; }

    public Func<FunctionRegistry, IFunctionExecutor>? ExecutorFactory { get; set; }

    public Func<ITrConfigRegistry, IPipelineExecutor>? PipelineFactory { get; set; }


    private readonly ConcurrentDictionary<string, IRegistry> _registries = new();
    private readonly List<IRegistryLifecycle> _lifecycles = new();
    private readonly IConflictResolver _conflictResolver;
    private bool _initialized;
    private bool _serviceProviderBuilt;

    public UnifiedRegistryCenter(IConflictResolver? conflictResolver = null)
    {
        _conflictResolver = conflictResolver ?? new DefaultConflictResolver();

        Functions = new FunctionRegistry();
        Workflows = null!;
        Executor = null!;
        Pipeline = null!;
    }


    public void RegisterRegistry<T>(string name, T registry) where T : IRegistry
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (_registries.TryGetValue(name, out var existing))
        {
            var strategy = _conflictResolver.Resolve(name, existing.GetType().Name, registry.GetType().Name);
            switch (strategy)
            {
                case ConflictStrategy.Overwrite:
                    if (existing is IRegistryLifecycle oldLifecycle)
                        _lifecycles.Remove(oldLifecycle);
                    break;
                case ConflictStrategy.Skip:
                    return;
                case ConflictStrategy.Throw:
                default:
                    throw new InvalidOperationException(
                        $"Registry '{name}' is already registered as {existing.GetType().Name}");
            }
        }

        _registries[name] = registry;
        if (registry is IRegistryLifecycle lifecycle)
        {
            _lifecycles.Add(lifecycle);
            if (_initialized && !lifecycle.IsInitialized)
                lifecycle.Initialize();
        }
    }

    public T GetRegistry<T>(string name) where T : IRegistry
    {
        if (_registries.TryGetValue(name, out var registry) && registry is T typed)
            return typed;
        throw new KeyNotFoundException($"Registry '{name}' of type {typeof(T).Name} not found");
    }

    public bool TryGetRegistry<T>(string name, [MaybeNullWhen(false)] out T registry) where T : IRegistry
    {
        if (_registries.TryGetValue(name, out var r) && r is T typed)
        {
            registry = typed;
            return true;
        }
        registry = default;
        return false;
    }


    public bool IsInitialized => _initialized;

    public void Initialize()
    {
        if (_initialized) return;

        EnsureCoreDependenciesCreated();

        foreach (var lifecycle in _lifecycles)
        {
            if (!lifecycle.IsInitialized)
                lifecycle.Initialize();
        }

        _initialized = true;
    }

    private void EnsureCoreDependenciesCreated()
    {
        if (Workflows != null) return;

        if (WorkflowFactory != null)
            Workflows = WorkflowFactory(Functions);
        else
            throw new InvalidOperationException(
                "WorkflowFactory 未设置。请在调用 Initialize() 前设置 UnifiedRegistryCenter.Instance.WorkflowFactory。");

        if (ExecutorFactory != null)
            Executor = ExecutorFactory(Functions);
        else
            throw new InvalidOperationException(
                "ExecutorFactory 未设置。请在调用 Initialize() 前设置 UnifiedRegistryCenter.Instance.ExecutorFactory。");

        if (PipelineFactory != null)
            Pipeline = PipelineFactory(Workflows);
        else
            throw new InvalidOperationException(
                "PipelineFactory 未设置。请在调用 Initialize() 前设置 UnifiedRegistryCenter.Instance.PipelineFactory。");
    }

    public void Shutdown()
    {
        if (!_initialized) return;
        foreach (var lifecycle in _lifecycles.Reverse<IRegistryLifecycle>())
        {
            if (lifecycle.IsInitialized)
                lifecycle.Shutdown();
        }
        _initialized = false;
    }


    public IServiceCollection CreateServiceCollection()
    {
        EnsureCoreDependenciesCreated();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton<ITrConfigRegistry>(Functions);
        services.AddSingleton<FunctionRegistry>(Functions);
        services.AddSingleton<IFunctionExecutor>(Executor);
        services.AddSingleton<IWorkflowRegistry>(Workflows);

        return services;
    }

    public IServiceProvider BuildServiceProvider(IServiceCollection? additionalServices = null)
    {
        if (_serviceProviderBuilt)
            throw new InvalidOperationException("BuildServiceProvider() 只能调用一次。");

        if (additionalServices != null)
        {
            var serviceProvider = additionalServices.BuildServiceProvider();
            _serviceProviderBuilt = true;
            return serviceProvider;
        }

        var services = CreateServiceCollection();
        var sp = services.BuildServiceProvider();
        _serviceProviderBuilt = true;
        return sp;
    }


    private Delegate? FindFirstDelegate(string name)
    {
        foreach (var (_, registry) in _registries)
        {
            if (registry is FunctionRegistry fr && fr.TryGetDelegate(name, out var del) && del != null)
                return del;
        }
        foreach (var (_, registry) in _registries)
        {
            if (registry is IMapRegistry<string, Delegate> mr && mr.TryGet(name, out var del) && del != null)
                return del;
        }
        return null;
    }

    public IFunctionDescriptor? ResolveFunction(string name)
    {
        foreach (var (regName, registry) in _registries)
        {
            if (registry is FunctionRegistry fr && fr.TryGetDescriptor(name, out var desc))
                return desc;
        }
        foreach (var (regName, registry) in _registries)
        {
            if (registry is IMapRegistry<string, Delegate> mr && mr.TryGet(name, out var del))
                return FunctionDescriptor.FromDelegate(del, $"Resolved from registry '{regName}'");
        }
        return null;
    }

    public bool TryExecuteFunction(string name, object?[]? args, out object? result)
    {
        result = null;
        var del = FindFirstDelegate(name);
        if (del == null) return false;
        try
        {
            result = del.DynamicInvoke(args);
            return true;
        }
        catch { return false; }
    }


    private static int GetRegistryCount(IRegistry registry) => registry switch
    {
        FunctionRegistry fr => fr.GetRegisteredNames().Count(),
        IMapRegistry<string, Delegate> mr => mr.Count,
        IMapRegistry<string, object> mr => mr.Count,
        ISetRegistry<string> sr => sr.Count,
        IFactoryRegistry<string, object> cr => cr.Count,
        _ => 0
    };

    private void ForEachRegistration(Action<string, string, string, string?> action)
    {
        foreach (var (regName, registry) in _registries)
        {
            var typeName = registry.GetType().Name;
            switch (registry)
            {
                case FunctionRegistry fr:
                    foreach (var funcName in fr.GetRegisteredNames())
                        action(funcName, regName, typeName, "Delegate");
                    break;
                case IMapRegistry<string, Delegate> mr:
                    foreach (var key in mr.GetKeys())
                        action(key, regName, typeName, "Delegate");
                    break;
                case IMapRegistry<string, object> mr:
                    foreach (var key in mr.GetKeys())
                        action(key, regName, typeName, "object");
                    break;
                case ISetRegistry<string> sr:
                    foreach (var key in sr.GetKeys())
                        action(key, regName, typeName, null);
                    break;
                case IFactoryRegistry<string, object> cr:
                    foreach (var key in cr.GetKeys())
                        action(key, regName, typeName, "Factory");
                    break;
            }
        }
    }

    public IReadOnlyList<DuplicateRegistration> FindDuplicates()
    {
        var nameMap = new Dictionary<string, List<RegistrationSource>>(StringComparer.OrdinalIgnoreCase);
        ForEachRegistration((name, regName, typeName, valueType) =>
        {
            if (!nameMap.ContainsKey(name))
                nameMap[name] = new List<RegistrationSource>();
            nameMap[name].Add(new RegistrationSource
            {
                RegistryName = regName,
                RegistryTypeName = typeName,
                ValueTypeName = valueType
            });
        });
        return nameMap
            .Where(kvp => kvp.Value.Count > 1)
            .Select(kvp => new DuplicateRegistration { Name = kvp.Key, Registrations = kvp.Value })
            .ToList();
    }

    public RegistryDiagnosticInfo Diagnose()
    {
        var info = new RegistryDiagnosticInfo();
        foreach (var (name, registry) in _registries)
            info.Add(name, new RegistryStats(GetRegistryCount(registry), registry.GetType().Name));
        info.Duplicates.AddRange(FindDuplicates());
        return info;
    }

    public void DumpAllRegistrations(TextWriter writer)
    {
        var diag = Diagnose();
        writer.WriteLine($"=== Registry Diagnostic ({diag.GeneratedAt:yyyy-MM-dd HH:mm:ss}) ===");
        writer.WriteLine();
        foreach (var (name, stats) in diag.Stats)
            writer.WriteLine($"  [{name}] ({stats.TypeName}) — {stats.Count} items");
        if (diag.Duplicates.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("=== Duplicate Registrations ===");
            foreach (var dup in diag.Duplicates)
            {
                writer.WriteLine($"  '{dup.Name}':");
                foreach (var src in dup.Registrations)
                    writer.WriteLine($"    - {src.RegistryName} ({src.RegistryTypeName})");
            }
        }
    }
}