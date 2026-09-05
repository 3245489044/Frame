
using TConfig;

namespace Modules.Registry;

public static class StrongTypedRegistrationExtensions
{
    public static void Register<TDelegate>(
        this ITrConfigRegistry registry,
        string name,
        TDelegate function,
        string? description = null)
        where TDelegate : Delegate
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(function);

        var descriptor = FunctionDescriptor.FromDelegate(function, description);
        descriptor.Name = name;
        descriptor.Delegate = function;

        registry.Register(name, descriptor);
    }

    public static void Register<TDelegate>(
        this ITrConfigRegistry registry,
        string name,
        TDelegate function,
        IReadOnlyList<IParamConfig> parameters,
        string? description = null)
        where TDelegate : Delegate
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(parameters);

        var descriptor = FunctionDescriptor.FromContract(function, parameters, description);
        descriptor.Name = name;
        descriptor.Delegate = function;
        registry.Register(name, descriptor);
    }
}