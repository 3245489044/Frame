
using Microsoft.Extensions.DependencyInjection;
using WorkFlowMethod.Parameters.Abstractions;

namespace WorkFlowMethod.Parameters;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddParameterServices(this IServiceCollection services)
    {
        services.AddSingleton<IParameterKeyMapper, ParameterKeyMapper>();

        services.AddSingleton<IParameterContextFactory, ParameterContextFactory>();

        services.AddSingleton<IParameterValueResolver, ParameterValueResolver>();

        services.AddSingleton<IParameterTypeConverter, ParameterTypeConverter>();


        services.AddSingleton<IParameterConfigProvider, ParameterConfigProvider>();

        return services;
    }
}
