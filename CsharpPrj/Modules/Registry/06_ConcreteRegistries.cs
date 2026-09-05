
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using TConfig;

namespace Modules.Registry;


public class FunctionRegistry : ITrConfigRegistry
{
    private readonly ConcurrentDictionary<string, ITrConfig> _configs;

    private readonly Dictionary<string, IFunctionUnit> _units;

    private readonly IConflictResolver _conflictResolver;

    public FunctionRegistry(
        IEqualityComparer<string>? comparer = null,
        IConflictResolver? conflictResolver = null)
    {
        var eq = comparer ?? StringComparer.OrdinalIgnoreCase;
        _configs = new ConcurrentDictionary<string, ITrConfig>(eq);
        _units = new Dictionary<string, IFunctionUnit>(eq);
        _conflictResolver = conflictResolver ?? new ConfigurableConflictResolver(ConflictStrategy.Overwrite);
    }

    private bool CheckConflict(string name)
    {
        if (!_configs.ContainsKey(name))
            return true;

        var strategy = _conflictResolver.Resolve(name, "existing", "new");
        switch (strategy)
        {
            case ConflictStrategy.Overwrite:
                return true;
            case ConflictStrategy.Skip:
                return false;
            case ConflictStrategy.Throw:
            default:
                throw new InvalidOperationException(
                    $"Function '{name}' is already registered. " +
                    "Use a different name or configure ConflictStrategy.Overwrite to allow overwriting.");
        }
    }


    public void Register(string name, Delegate function)
    {
        Register(name, function, (string?)null);
    }

    public Delegate? Find(string functionName)
    {
        if (_configs.TryGetValue(functionName, out var config))
        {
            return config switch
            {
                FunctionDescriptor fd => fd.Delegate,
                _ => null
            };
        }
        return null;
    }

    public IEnumerable<string> GetAllFunctionNames() => _configs.Keys;


    public void Register(string name, Delegate function, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Function name cannot be empty", nameof(name));

        ArgumentNullException.ThrowIfNull(function);

        if (!CheckConflict(name))
            return;

        var descriptor = FunctionDescriptor.FromDelegate(function, description);
        descriptor.Name = name;
        descriptor.Delegate = function;
        _configs[name] = descriptor;
    }

    public void Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Function name cannot be empty", nameof(name));

        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(parameters);

        if (!CheckConflict(name))
            return;

        var descriptor = FunctionDescriptor.FromContract(function, parameters, description);
        descriptor.Name = name;
        _configs[name] = descriptor;
    }


    public void Register(string name, IFunctionUnit unit, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Function name cannot be empty", nameof(name));

        ArgumentNullException.ThrowIfNull(unit);

        if (!CheckConflict(name))
            return;

        _units[name] = unit;

        Func<object?, Task<ExecutionResult>> wrappedDelegate = async (input) =>
        {
            return await unit.ExecuteAsync(input);
        };

        var descriptor = FunctionDescriptor.FromDelegate(wrappedDelegate, description);
        descriptor.Name = name;
        _configs[name] = descriptor;
    }


    public void Register(string name, ITrConfig config)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Config name cannot be empty", nameof(name));

        ArgumentNullException.ThrowIfNull(config);

        if (!CheckConflict(name))
            return;

        _configs[name] = config;
    }


    public bool TryGetConfig(string name, out ITrConfig config)
        => _configs.TryGetValue(name, out config);

    public bool TryGetDescriptor(string name, out IFunctionDescriptor descriptor)
    {
        if (_configs.TryGetValue(name, out var config) && config is IFunctionDescriptor fd)
        {
            descriptor = fd;
            return true;
        }

        descriptor = null!;
        return false;
    }

    public bool TryGetDelegate(string name, out Delegate? function)
    {
        if (_configs.TryGetValue(name, out var config))
        {
            function = config switch
            {
                FunctionDescriptor fd => fd.Delegate,
                _ => null
            };
            return function != null;
        }

        function = null;
        return false;
    }

    public bool TryGetUnit(string name, out IFunctionUnit? unit)
        => _units.TryGetValue(name, out unit);

    public bool Contains(string name)
        => _configs.ContainsKey(name);

    public IEnumerable<string> GetRegisteredNames()
        => _configs.Keys;


    public void Unregister(string name)
    {
        _configs.TryRemove(name, out _);
        _units.Remove(name);
    }

    public void Clear()
    {
        _configs.Clear();
        _units.Clear();
    }
}




public class NormalizationRegistry : MapRegistryBase<string, string>
{
    public static readonly Dictionary<string, string> DefaultMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["click"] = "do click",
        ["set"] = "do set",
        ["type"] = "do type",
        ["select"] = "do select",
        ["check"] = "do check",
        ["uncheck"] = "do uncheck",
    };

    public NormalizationRegistry(
        IEqualityComparer<string>? comparer = null,
        bool allowOverwrite = true,
        bool includeDefaults = true)
        : base(comparer ?? StringComparer.OrdinalIgnoreCase, allowOverwrite)
    {
        if (includeDefaults)
        {
            foreach (var kvp in DefaultMappings)
                Register(kvp.Key, kvp.Value);
        }
    }

}
