using System.Diagnostics.CodeAnalysis;
using TConfig;

namespace ExecutionFramework.Registry;

public class OverridableFunctionRegistry : IWorkflowRegistry
{
    private readonly ITrConfigRegistry _inner;
    private readonly Dictionary<string, (Delegate Function, string? Description)> _overrides = new();

    public OverridableFunctionRegistry(ITrConfigRegistry inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public bool IsOverrideEnabled { get; set; } = true;

    public void Override(string name, Delegate function, string? description = null)
    {
        _overrides[name] = (function, description);
    }

    public void RemoveOverride(string name)
    {
        _overrides.Remove(name);
    }

    public void ClearOverrides()
    {
        _overrides.Clear();
    }

    public bool HasOverride(string name)
        => _overrides.ContainsKey(name);

    public IEnumerable<string> GetOverriddenNames()
        => _overrides.Keys;

    public void Register(string name, ITrConfig config)
    {
        if (IsOverrideEnabled && _overrides.ContainsKey(name))
        {
            return;
        }
        _inner.Register(name, config);
    }

    public void Register(string name, Delegate function, string? description = null)
    {
        if (IsOverrideEnabled && _overrides.ContainsKey(name))
        {
            return;
        }
        _inner.Register(name, function, description);
    }

    public void Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null)
    {
        if (IsOverrideEnabled && _overrides.ContainsKey(name))
        {
            return;
        }
        _inner.Register(name, function, parameters, description);
    }

    public void Unregister(string name)
    {
        _overrides.Remove(name);
        _inner.Unregister(name);
    }

    public bool TryGetConfig(string name, out ITrConfig config)
    {
        if (IsOverrideEnabled && _overrides.TryGetValue(name, out var overrideEntry))
        {
            config = FunctionDescriptor.FromDelegate(overrideEntry.Function, overrideEntry.Description ?? name);
            return true;
        }
        return _inner.TryGetConfig(name, out config);
    }

    public bool Contains(string name)
    {
        if (IsOverrideEnabled && _overrides.ContainsKey(name))
            return true;
        return _inner.Contains(name);
    }

    public IEnumerable<string> GetRegisteredNames()
    {
        var names = new HashSet<string>(_inner.GetRegisteredNames());
        if (IsOverrideEnabled)
        {
            foreach (var key in _overrides.Keys)
                names.Add(key);
        }
        return names;
    }

    public void Clear()
    {
        _overrides.Clear();
        _inner.Clear();
    }


    public Delegate? Find(string functionName)
    {
        if (IsOverrideEnabled && _overrides.TryGetValue(functionName, out var overrideEntry))
            return overrideEntry.Function;
        return _inner.Find(functionName);
    }

    public bool TryGetDescriptor(string name, out IFunctionDescriptor? descriptor)
    {
        if (IsOverrideEnabled && _overrides.TryGetValue(name, out var overrideEntry))
        {
            descriptor = FunctionDescriptor.FromDelegate(overrideEntry.Function, overrideEntry.Description ?? name);
            return true;
        }
        return _inner.TryGetDescriptor(name, out descriptor);
    }

    public bool TryGetDelegate(string name, out Delegate? function)
    {
        if (IsOverrideEnabled && _overrides.TryGetValue(name, out var overrideEntry))
        {
            function = overrideEntry.Function;
            return true;
        }
        return _inner.TryGetDelegate(name, out function);
    }

    public object? Call(string name, params object?[] args)
    {
        if (IsOverrideEnabled && _overrides.TryGetValue(name, out var overrideEntry))
            return overrideEntry.Function.DynamicInvoke(args);
        if (!_inner.TryGetConfig(name, out var config))
            throw new TConfig.FunctionNotFoundException(name);
        return config.Invoke(args);
    }

    public async Task<object?> CallAsync(string name, params object?[] args)
    {
        if (IsOverrideEnabled && _overrides.TryGetValue(name, out var overrideEntry))
            return await TaskHelper.InvokeAndUnwrapAsync(overrideEntry.Function, args);
        if (!_inner.TryGetConfig(name, out var config))
            throw new TConfig.FunctionNotFoundException(name);
        return await TaskHelper.InvokeAndUnwrapAsync(config, args);
    }

    public async Task<object?> CallWithContextAsync(string name, FunctionContext context, params object?[] args)
    {
        object?[] mergedArgs = args;
        if ((args == null || args.Length == 0) && context?.Parameters != null && context.Parameters.Count > 0)
        {
            mergedArgs = new object?[context.Parameters.Count];
            int i = 0;
            foreach (var kvp in context.Parameters)
            {
                mergedArgs[i++] = kvp.Value;
            }
        }

        if (IsOverrideEnabled && _overrides.TryGetValue(name, out var overrideEntry))
            return await TaskHelper.InvokeAndUnwrapAsync(overrideEntry.Function, mergedArgs);
        if (!_inner.TryGetConfig(name, out var config))
            throw new TConfig.FunctionNotFoundException(name);
        return await TaskHelper.InvokeAndUnwrapAsync(config, mergedArgs);
    }

    public bool HasFunction(string name)
        => Contains(name);

    public IEnumerable<string> GetFunctionNames()
        => GetRegisteredNames();
}
