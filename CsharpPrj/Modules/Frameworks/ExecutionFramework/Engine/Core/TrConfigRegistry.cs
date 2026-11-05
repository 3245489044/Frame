using System.Collections.Concurrent;
using TConfig;

namespace ExecutionFramework.Engine;

public class TrConfigRegistry : ITrConfigRegistry
{
    private readonly ConcurrentDictionary<string, ITrConfig> _configs = new();

    public void Register(string name, ITrConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _configs[name] = config;
    }

    public void Register(string name, Delegate function, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        var config = new DelegateTrConfig(name, function, description);
        _configs[name] = config;
    }

    public void Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(parameters);
        var config = new DelegateTrConfig(name, function, description, parameters);
        _configs[name] = config;
    }

    public void Unregister(string name)
    {
        _configs.TryRemove(name, out _);
    }

    public bool TryGetConfig(string name, out ITrConfig config)
    {
        return _configs.TryGetValue(name, out config);
    }

    public bool Contains(string name)
    {
        return _configs.ContainsKey(name);
    }

    public IEnumerable<string> GetRegisteredNames()
    {
        return _configs.Keys;
    }

    public void Clear()
    {
        _configs.Clear();
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

    public bool TryGetDescriptor(string name, out IFunctionDescriptor? descriptor)
    {
        if (_configs.TryGetValue(name, out var config) && config is IFunctionDescriptor fd)
        {
            descriptor = fd;
            return true;
        }

        descriptor = null;
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

    private class DelegateTrConfig : ITrConfig
    {
        private readonly Delegate _function;
        private readonly IReadOnlyList<IParamConfig> _parameters;

        public string Name { get; }
        public string Description { get; }
        public Type InputType { get; }
        public Type OutputType { get; }
        public Type ReturnType => OutputType;
        public bool IsAsync { get; }
        public IReadOnlyList<IParamConfig> Parameters => _parameters;

        public DelegateTrConfig(string name, Delegate function, string? description = null, IReadOnlyList<IParamConfig>? parameters = null)
        {
            Name = name;
            _function = function;
            Description = description ?? string.Empty;

            var method = function.Method;
            var paramInfos = method.GetParameters();
            InputType = paramInfos.Length > 0 ? paramInfos[0].ParameterType : typeof(object);
            OutputType = method.ReturnType;
            IsAsync = method.ReturnType == typeof(Task) ||
                      (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>));

            _parameters = parameters ?? Array.Empty<IParamConfig>();
        }

        public object? Invoke(params object?[]? args)
        {
            return _function.DynamicInvoke(args);
        }
    }
}
