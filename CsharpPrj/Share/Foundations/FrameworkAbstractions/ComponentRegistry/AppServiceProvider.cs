

using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace FrameworkAbstractions.ComponentRegistry
{
    public static class AppServiceProvider
    {
        private static IServiceProvider? _serviceProvider;
        private static readonly ConcurrentDictionary<Type, object> _fallbackRegistrations = new();

        private static readonly object _lock = new();

        public static IServiceProvider Current
        {
            get => _serviceProvider ?? throw new InvalidOperationException(
                "AppServiceProvider 尚未初始化。请在应用启动时调用 AppServiceProvider.Initialize(serviceProvider)。");
            private set => _serviceProvider = value;
        }

        public static void Initialize(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public static TService? Resolve<TService>()
        {
            if (_serviceProvider != null)
            {
                var service = _serviceProvider.GetService<TService>();
                if (service != null)
                    return service;
            }

            if (_fallbackRegistrations.TryGetValue(typeof(TService), out var instance))
            {
                return (TService)instance;
            }

            return default;
        }

        public static object? Resolve(Type serviceType)
        {
            if (serviceType == null)
                throw new ArgumentNullException(nameof(serviceType));

            if (_serviceProvider != null)
            {
                var service = _serviceProvider.GetService(serviceType);
                if (service != null)
                    return service;
            }

            if (_fallbackRegistrations.TryGetValue(serviceType, out var instance))
            {
                return instance;
            }

            return null;
        }

        public static TService ResolveRequired<TService>() where TService : class
        {
            var instance = Resolve<TService>();
            if (instance == null)
                throw new InvalidOperationException(
                    $"服务 {typeof(TService).Name} 未注册。请确保已在 DI 容器中注册。");
            return instance;
        }

        public static void Register<TService>(TService instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            _fallbackRegistrations[typeof(TService)] = instance;
        }

        public static void Register(Type serviceType, object instance)
        {
            if (serviceType == null)
                throw new ArgumentNullException(nameof(serviceType));
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            _fallbackRegistrations[serviceType] = instance;
        }

        public static bool IsRegistered<TService>() where TService : class
        {
            if (_serviceProvider?.GetService<TService>() != null)
                return true;

            return _fallbackRegistrations.ContainsKey(typeof(TService));
        }

        public static bool IsRegistered(Type serviceType)
        {
            if (_serviceProvider?.GetService(serviceType) != null)
                return true;

            return _fallbackRegistrations.ContainsKey(serviceType);
        }

        public static HealthCheckReport ValidateAll()
        {
            var report = new HealthCheckReport();

            if (_serviceProvider != null)
            {
                foreach (var kvp in _fallbackRegistrations)
                {
                    var reg = new ComponentRegistration
                    {
                        ServiceType = kvp.Key,
                        ServiceName = kvp.Key.Name,
                        Instance = kvp.Value,
                        Criticality = CriticalityLevel.Medium,
                        RegisteredAt = DateTime.UtcNow
                    };
                    report.Registered.Add(reg);
                }
            }
            else
            {
                foreach (var kvp in _fallbackRegistrations)
                {
                    var reg = new ComponentRegistration
                    {
                        ServiceType = kvp.Key,
                        ServiceName = kvp.Key.Name,
                        Instance = kvp.Value,
                        Criticality = CriticalityLevel.Medium,
                        RegisteredAt = DateTime.UtcNow
                    };
                    report.Registered.Add(reg);
                }
            }

            report.AllCriticalRegistered = report.MissingCritical.Count == 0;
            return report;
        }

        public static void Clear()
        {
            _fallbackRegistrations.Clear();
            _serviceProvider = null;
        }
    }
}
