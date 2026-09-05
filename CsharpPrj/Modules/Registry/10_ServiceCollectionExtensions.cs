
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modules.Registry;

public static class RegistryServiceCollectionExtensions
{
    public static IServiceCollection AddRegistryFramework(this IServiceCollection services)
    {
        services.TryAddSingleton<IConflictResolver, DefaultConflictResolver>();

        services.TryAddSingleton<UnifiedRegistryCenter>(sp =>
        {
            var resolver = sp.GetRequiredService<IConflictResolver>();
            return new UnifiedRegistryCenter(resolver);
        });

        services.TryAddSingleton<IRegistryManager>(sp =>
            sp.GetRequiredService<UnifiedRegistryCenter>());
        services.TryAddSingleton<IRegistryQuery>(sp =>
            sp.GetRequiredService<UnifiedRegistryCenter>());
        services.TryAddSingleton<IRegistryDiagnostics>(sp =>
            sp.GetRequiredService<UnifiedRegistryCenter>());
        services.TryAddSingleton<IRegistryLifecycle>(sp =>
            sp.GetRequiredService<UnifiedRegistryCenter>());

        return services;
    }

    public static IServiceCollection AddRegistryFramework(
        this IServiceCollection services,
        IConflictResolver conflictResolver)
    {
        services.TryAddSingleton(conflictResolver);

        services.TryAddSingleton<UnifiedRegistryCenter>(sp =>
        {
            return new UnifiedRegistryCenter(conflictResolver);
        });

        services.TryAddSingleton<IRegistryManager>(sp =>
            sp.GetRequiredService<UnifiedRegistryCenter>());
        services.TryAddSingleton<IRegistryQuery>(sp =>
            sp.GetRequiredService<UnifiedRegistryCenter>());
        services.TryAddSingleton<IRegistryDiagnostics>(sp =>
            sp.GetRequiredService<UnifiedRegistryCenter>());
        services.TryAddSingleton<IRegistryLifecycle>(sp =>
            sp.GetRequiredService<UnifiedRegistryCenter>());

        return services;
    }
}
