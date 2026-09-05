
namespace TConfig
{
    public interface IParamConfig
    {
        string Name { get; }

        Type Type { get; }

        bool IsOptional { get; }

        object? DefaultValue { get; }
    }

    public interface ITrConfig
    {
        string Name { get; }

        string Description { get; }

        Type InputType { get; }

        Type OutputType { get; }

        Type ReturnType { get; }

        bool IsAsync { get; }

        IReadOnlyList<IParamConfig> Parameters { get; }

        object? Invoke(params object?[] args);
    }

    public interface ITrConfigRegistry
    {
        void Register(string name, ITrConfig config);

        void Register(string name, Delegate function, string? description = null);

        void Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null);

        void Unregister(string name);

        bool TryGetConfig(string name, out ITrConfig config);

        bool Contains(string name);

        IEnumerable<string> GetRegisteredNames();

        void Clear();


        Delegate? Find(string functionName);

        bool TryGetDescriptor(string name, out IFunctionDescriptor? descriptor);

        bool TryGetDelegate(string name, out Delegate? function);
    }

    public interface ITrCall
    {
        string ConfigName { get; }

        List<object?> Parameters { get; }

        ITrCall? NestedCall { get; set; }

        int NestedCallParameterIndex { get; set; }

        TrPipeline? SubPipeline { get; set; }

        bool UseExplicitParameters { get; set; }

        Dictionary<string, object> Metadata { get; }
    }
}
