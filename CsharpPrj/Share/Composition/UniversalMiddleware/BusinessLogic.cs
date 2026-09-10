
#nullable enable
using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using UniversalMiddleware.Abstractions;
using UniversalMiddleware.Core;
using UniversalMiddleware.Serialization;
using CommandFramework.Parsing;
using CommandFramework.Models;

namespace UniversalMiddleware.Binding
{
    using UniversalMiddleware.Abstractions;

    public class DataBindingManager : IDataBindingManager
    {
        private readonly ConcurrentDictionary<string, object?> _dataSources = new();
        private readonly ConcurrentDictionary<string, List<Action<object?>>> _propertyHandlers = new();

        public void RegisterDataSource<T>(string key, T dataSource) where T : class, INotifyPropertyChanged
        {
            _dataSources[key] = dataSource;
            dataSource.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName != null)
                    NotifyPropertyChanged(key, e.PropertyName, dataSource);
            };
        }

        public T? GetDataSource<T>(string key) where T : class
            => _dataSources.TryGetValue(key, out var ds) ? ds as T : null;

        public void UnregisterDataSource(string key)
            => _dataSources.TryRemove(key, out _);

        public void BindProperty<T>(string dataSourceKey, string propertyName, Action<T> onChanged)
        {
            var handlerKey = $"{dataSourceKey}.{propertyName}";

            Action<object?> wrappedHandler = value =>
            {
                if (value is T typedValue)
                    onChanged(typedValue);
            };

            _propertyHandlers.AddOrUpdate(handlerKey,
                _ => new List<Action<object?>> { wrappedHandler },
                (_, list) => { list.Add(wrappedHandler); return list; });

            if (_dataSources.TryGetValue(dataSourceKey, out var ds) && ds != null)
            {
                var value = GetPropertyValue(ds, propertyName);
                if (value is T typedValue)
                    onChanged(typedValue);
            }
        }

        public void UnbindProperty(string dataSourceKey, string propertyName)
        {
            var handlerKey = $"{dataSourceKey}.{propertyName}";
            _propertyHandlers.TryRemove(handlerKey, out _);
        }

        public void ClearAll()
        {
            _dataSources.Clear();
            _propertyHandlers.Clear();
        }

        private static object? GetPropertyValue(object obj, string propertyName)
        {
            var prop = obj.GetType().GetProperty(propertyName);
            return prop?.GetValue(obj);
        }

        private void NotifyPropertyChanged(string dataSourceKey, string propertyName, object dataSource)
        {
            var handlerKey = $"{dataSourceKey}.{propertyName}";
            if (_propertyHandlers.TryGetValue(handlerKey, out var handlers))
            {
                var value = GetPropertyValue(dataSource, propertyName);

                foreach (var handler in handlers.ToArray())
                {
                    handler(value);
                }
            }
        }
    }
}

namespace UniversalMiddleware.Controls
{
    using UniversalMiddleware.Abstractions;
    using UniversalMiddleware.Core;
    using UniversalMiddleware.Middleware;

    public class GenericControlFactory : IConfigDrivenFactory<ControlDescriptor, object>
    {
        private readonly Dictionary<string, Func<ControlDescriptor, object>> _builders = new();
        private readonly Microsoft.Extensions.Logging.ILogger? _logger;

        public GenericControlFactory(Microsoft.Extensions.Logging.ILogger? logger = null) => _logger = logger;

        public void RegisterBuilder(string type, Func<ControlDescriptor, object> builder)
            => _builders[type.ToLowerInvariant()] = builder;

        public object Create(ControlDescriptor config)
        {
            var key = config.Type.ToLowerInvariant();
            if (_builders.TryGetValue(key, out var builder))
            {
                _logger?.LogDebug($"GenericControlFactory creating '{config.Type}' named '{config.Name}'");
                return builder(config);
            }
            throw new KeyNotFoundException($"No builder registered for control type '{config.Type}'");
        }

        public IEnumerable<object> CreateMany(IEnumerable<ControlDescriptor> configs)
            => configs.Select<ControlDescriptor, object>(Create);

        public bool HasBuilder(string type) => _builders.ContainsKey(type.ToLowerInvariant());
    }
}

namespace UniversalMiddleware.Engine
{
    using UniversalMiddleware.Abstractions;

    public class SmartLayoutEngine : ISmartLayoutEngine
    {
        private readonly global::UniversalLayoutFramework.LayoutEngine _innerEngine;
        private readonly Microsoft.Extensions.Logging.ILogger? _logger;
        private readonly ConcurrentDictionary<string, global::UniversalLayoutFramework.SmartLayoutConfig> _attached = new();

        public SmartLayoutEngine(
            ILayoutCalculator? calculator = null,
            ILayoutValidator? validator = null,
            Microsoft.Extensions.Logging.ILogger? logger = null)
        {
            _logger = logger;

            if (calculator != null)
                logger?.LogWarning("SmartLayoutEngine: calculator 参数已废弃，框架内部有独立实现。传入的值将被忽略。");
            if (validator != null)
                logger?.LogWarning("SmartLayoutEngine: validator 参数已废弃，框架内部有独立实现。传入的值将被忽略。");

            var builder = new global::UniversalLayoutFramework.LayoutEngineBuilder()
                .UseProportionalLayout()
                .UseStackLayout()
                .UsePushRepair();

            if (logger != null)
            {
                builder.WithLogger(new FrameworkLoggerAdapter(logger));
            }

            _innerEngine = builder.Build();
        }

        public void Apply(global::UniversalLayoutFramework.ILayoutContainer container, object? config = null)
        {
            var smartConfig = ConvertConfig(config);
            _innerEngine.Apply(container, smartConfig);
        }

        public void Refresh(global::UniversalLayoutFramework.ILayoutContainer container)
        {
            if (_attached.TryGetValue(container.Name, out var config))
            {
                _innerEngine.Apply(container, config);
            }
        }

        public void Attach(global::UniversalLayoutFramework.ILayoutContainer container, object? config = null)
        {
            var smartConfig = ConvertConfig(config);
            _attached[container.Name] = smartConfig;
            _innerEngine.Attach(container, smartConfig);
        }

        public void Detach(global::UniversalLayoutFramework.ILayoutContainer container)
        {
            _attached.TryRemove(container.Name, out _);
            _innerEngine.Detach(container);
        }

        public void NotifyResize(global::UniversalLayoutFramework.ILayoutContainer container)
        {
            _innerEngine.NotifyResize(container);
        }

        private global::UniversalLayoutFramework.SmartLayoutConfig ConvertConfig(object? config)
        {
            if (config is global::UniversalLayoutFramework.SmartLayoutConfig frameworkConfig)
                return frameworkConfig;

            if (config != null)
            {
                try
                {
                    var result = new global::UniversalLayoutFramework.SmartLayoutConfig
                    {
                        LogEnabled = _logger != null
                    };

                    if (config is System.Text.Json.Nodes.JsonObject jsonObj)
                    {
                        if (jsonObj.TryGetPropertyValue("logEnabled", out var logNode) && logNode is System.Text.Json.Nodes.JsonValue logVal)
                            result.LogEnabled = (bool)logVal;
                        if (jsonObj.TryGetPropertyValue("enableResizeHandling", out var resizeNode) && resizeNode is System.Text.Json.Nodes.JsonValue resizeVal)
                            result.EnableResizeHandling = (bool)resizeVal;
                        if (jsonObj.TryGetPropertyValue("recursive", out var recNode) && recNode is System.Text.Json.Nodes.JsonValue recVal)
                            result.Recursive = (bool)recVal;
                        if (jsonObj.TryGetPropertyValue("debounceInterval", out var debNode) && debNode is System.Text.Json.Nodes.JsonValue debVal)
                            result.DebounceInterval = TimeSpan.FromMilliseconds((int)debVal);
                    }
                    else if (config is System.Collections.IDictionary dict)
                    {
                        if (dict.Contains("logEnabled"))
                            result.LogEnabled = Convert.ToBoolean(dict["logEnabled"]);
                        if (dict.Contains("enableResizeHandling"))
                            result.EnableResizeHandling = Convert.ToBoolean(dict["enableResizeHandling"]);
                        if (dict.Contains("recursive"))
                            result.Recursive = Convert.ToBoolean(dict["recursive"]);
                        if (dict.Contains("debounceInterval"))
                            result.DebounceInterval = TimeSpan.FromMilliseconds(Convert.ToInt32(dict["debounceInterval"]));
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"SmartLayoutEngine.ConvertConfig: 无法转换配置类型 {config.GetType().Name}，使用默认配置。错误: {ex.Message}");
                }
            }

            return new global::UniversalLayoutFramework.SmartLayoutConfig
            {
                LogEnabled = _logger != null
            };
        }

        private class FrameworkLoggerAdapter : global::UniversalLayoutFramework.ILayoutLogger
        {
            private readonly Microsoft.Extensions.Logging.ILogger _logger;

            public FrameworkLoggerAdapter(Microsoft.Extensions.Logging.ILogger logger) => _logger = logger;

            public void Log(global::UniversalLayoutFramework.LogLevel level, string message)
            {
                switch (level)
                {
                    case global::UniversalLayoutFramework.LogLevel.Trace:
                    case global::UniversalLayoutFramework.LogLevel.Debug:
                        _logger.LogDebug(message);
                        break;
                    case global::UniversalLayoutFramework.LogLevel.Information:
                        _logger.LogInformation(message);
                        break;
                    case global::UniversalLayoutFramework.LogLevel.Warning:
                        _logger.LogWarning(message);
                        break;
                    case global::UniversalLayoutFramework.LogLevel.Error:
                    case global::UniversalLayoutFramework.LogLevel.Critical:
                        _logger.LogError(message);
                        break;
                }
            }

            public bool IsEnabled(global::UniversalLayoutFramework.LogLevel level) => true;
        }
    }
}

