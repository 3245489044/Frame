
using Container.Abstractions;
using Container.Behaviors;
using Container.Core;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Container.Extensions;

public class ContainerOptions
{
    public bool EnableRetry { get; set; } = true;

    public bool EnableCircuitBreaker { get; set; } = false;

    public bool EnableTimeout { get; set; } = true;

    public bool EnableLogging { get; set; } = true;

    public int MaxRetries { get; set; } = 3;

    public int RetryDelayMs { get; set; } = 100;

    public int TimeoutSeconds { get; set; } = 30;

    public int CircuitBreakerThreshold { get; set; } = 5;

    public int CircuitBreakerDurationSeconds { get; set; } = 30;

    public bool IsEventMode { get; set; } = false;

    public int EventTimeoutSeconds { get; set; } = 10;
}


public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddContainer(
        this IServiceCollection services,
        Action<ContainerOptions>? configure = null)
    {
        var options = new ContainerOptions();
        configure?.Invoke(options);

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<ExecuteCommandHandler>();

            if (options.EnableLogging)
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

            if (options.EnableTimeout)
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(TimeoutBehavior<,>));

            if (options.EnableRetry)
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));

            if (options.EnableCircuitBreaker)
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(CircuitBreakerBehavior<,>));
        });

        services.AddTransient<IExecutionCell>(sp =>
        {
            var mediator = sp.GetRequiredService<IMediator>();
            return new ExecutionCell(mediator);
        });

        services.AddTransient<IExecutionEngine, ExecutionEngine>();

        return services;
    }


    public static IServiceCollection AddContainerForUI(
        this IServiceCollection services,
        Action<ContainerOptions>? configure = null)
    {
        services.AddContainer(options =>
        {
            options.IsEventMode = true;
            options.TimeoutSeconds = options.EventTimeoutSeconds;
            options.EnableRetry = false;
            options.EnableCircuitBreaker = true;
            options.CircuitBreakerThreshold = 3;
            options.CircuitBreakerDurationSeconds = 15;

            configure?.Invoke(options);
        });


        services.AddTransient<IExecutionCell>(sp =>
        {
            var innerCell = new ExecutionCell(sp.GetRequiredService<MediatR.IMediator>());
            return new BackgroundExecutionCell(innerCell);
        });

        return services;
    }

}


