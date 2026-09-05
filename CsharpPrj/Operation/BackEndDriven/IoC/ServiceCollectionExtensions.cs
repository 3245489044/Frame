
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modules.EventFramework.Abstractions;
using Share.EventBindingOrchestrator.FunctionHub;

namespace BackEndDriven.IoC;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEventBindingServices(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IFunctionResolver, FunctionResolver>();

        services.TryAddSingleton<IParameterBinder, ParameterBinder>();

        services.TryAddSingleton<TConfig.IEventBinder, EventBinder>();

        services.TryAddSingleton<IFunctionHub, FunctionHub>();

        return services;
    }
}
