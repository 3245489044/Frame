
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParameterResolver.Abstractions;
using ParameterResolver.Converter;
using ParameterResolver.Strategies;
using ParameterResolver.Strategies.Abstractions;

namespace ParameterResolver.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddParameterResolver(
        this IServiceCollection services,
        Action<ParameterResolverOptions>? configure = null)
    {
        var options = new ParameterResolverOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IConverterRegistry>(sp =>
        {
            var registry = new ConverterRegistry();

            registry.RegisterProvider(new BuiltInConverterProvider());

            if (options.ScanAssemblies)
            {
                registry.RegisterProvider(new AssemblyScannerProvider(true));
            }

            foreach (var (type, converter) in options.Converters)
            {
                registry.Register(type, converter);
            }

            return registry;
        });

        services.TryAddSingleton<ITypeConversionService, TypeConversionService>();

        return services;
    }

    public static IServiceCollection AddStrategyResolver<TStrategy, TAdapter>(
        this IServiceCollection services,
        StrategyResolver<TStrategy, TAdapter> resolver)
        where TStrategy : class
        where TAdapter : class
    {
        services.TryAddSingleton(resolver);
        return services;
    }
}

public class ParameterResolverOptions
{
    public bool ScanAssemblies { get; set; }

    internal List<KeyValuePair<Type, IValueConverter>> Converters { get; } = new();

    public ParameterResolverOptions AddConverter<TTarget>(IValueConverter converter)
    {
        Converters.Add(new KeyValuePair<Type, IValueConverter>(typeof(TTarget), converter));
        return this;
    }

    public ParameterResolverOptions AddConverter(Type targetType, IValueConverter converter)
    {
        Converters.Add(new KeyValuePair<Type, IValueConverter>(targetType, converter));
        return this;
    }
}
