
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using TConfig;
using Common.PlaceholderFramework.SchemaValidation;
using FrontEndDriven.Diagnostics;

namespace FrontEndDriven.Shared
{
    public class ControlConfigTarget : IJsonSchemaTarget
    {
        private static readonly ConcurrentDictionary<string, PropertyEntry> AccessorCache
            = BuildAccessorCache();

        private sealed record PropertyEntry(
            Func<ControlConfig, object?> Accessor,
            PropertyInfo Property);

        private static ConcurrentDictionary<string, PropertyEntry> BuildAccessorCache()
        {
            var cache = new ConcurrentDictionary<string, PropertyEntry>(StringComparer.OrdinalIgnoreCase);
            var configType = typeof(ControlConfig);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
            var param = Expression.Parameter(configType, "config");

            foreach (var prop in configType.GetProperties(flags))
            {
                if (prop.GetIndexParameters().Length > 0) continue;

                try
                {
                    var convert = Expression.Convert(
                        Expression.Property(param, prop),
                        typeof(object));
                    var accessor = Expression.Lambda<Func<ControlConfig, object?>>(convert, param).Compile();
                    cache[prop.Name] = new PropertyEntry(accessor, prop);
                }
                catch (Exception ex)
                {
                    // E8 修复：空 catch 显式化——个别属性表达式编译失败仅记日志并跳过（不阻断其余属性注册）
                    FrontEndDrivenLog.Log("ControlConfigTarget_BuildAccessorCache_skip",
                        new { property = prop.Name, error = ex.Message, exceptionType = ex.GetType().Name });
                }
            }
            return cache;
        }

        private readonly ControlConfig _config;

        public ControlConfigTarget(ControlConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public string TargetName => _config.Name ?? "(unnamed)";

        public string TargetType => _config.Type ?? "(unknown)";

        public IEnumerable<IJsonSchemaTarget> GetChildren()
        {
            if (_config.Controls != null)
            {
                foreach (var child in _config.Controls)
                    yield return new ControlConfigTarget(child);
            }

            if (_config.Panel1Controls != null)
            {
                foreach (var child in _config.Panel1Controls)
                    yield return new ControlConfigTarget(child);
            }

            if (_config.Panel2Controls != null)
            {
                foreach (var child in _config.Panel2Controls)
                    yield return new ControlConfigTarget(child);
            }
        }

        public bool HasField(string fieldName)
        {
            if (_config == null || string.IsNullOrEmpty(fieldName))
                return false;

            if (fieldName.Contains('.'))
            {
                var parts = fieldName.Split('.', 2);
                var parentField = parts[0];
                var childField = parts[1];

                return parentField.ToLowerInvariant() switch
                {
                    "container" when _config.Container != null =>
                        HasContainerField(_config.Container, childField),
                    "layout" when _config.Layout != null =>
                        HasLayoutPositionField(_config.Layout, childField),
                    _ => false
                };
            }

            if (AccessorCache.TryGetValue(fieldName, out var entry))
            {
                return EvaluateHasField(entry.Property, entry.Accessor(_config), _config, fieldName);
            }

            var fallbackProp = typeof(ControlConfig).GetProperty(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (fallbackProp != null)
            {
                return EvaluateHasField(fallbackProp, fallbackProp.GetValue(_config), _config, fieldName);
            }

            return false;
        }

        private static bool EvaluateHasField(PropertyInfo prop, object? value, ControlConfig config, string fieldName)
        {
            if (prop == null) return false;

            if (prop.PropertyType == typeof(string))
                return value != null && !string.IsNullOrEmpty((string)value);

            if (prop.PropertyType.IsGenericType &&
                prop.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                return value != null;

            if (typeof(System.Collections.IList).IsAssignableFrom(prop.PropertyType))
                return value is System.Collections.IList list && list.Count > 0;

            if (prop.PropertyType.IsValueType)
            {
                return config.ExtensionData?.Keys
                    .Any(k => string.Equals(k, fieldName, StringComparison.OrdinalIgnoreCase)) == true;
            }

            return value != null;
        }

        public string? GetFieldValue(string fieldName)
        {
            if (_config == null || string.IsNullOrEmpty(fieldName))
                return null;

            if (fieldName.Contains('.'))
            {
                var parts = fieldName.Split('.', 2);
                var parentField = parts[0];
                var childField = parts[1];

                return parentField.ToLowerInvariant() switch
                {
                    "container" when _config.Container != null =>
                        GetContainerFieldValue(_config.Container, childField),
                    "layout" when _config.Layout != null =>
                        GetLayoutPositionFieldValue(_config.Layout, childField),
                    _ => null
                };
            }

            // R7：AccessorCache 为 ControlConfig 全部 public 属性（IgnoreCase）编译缓存，
            // 未命中即属性不存在——删除冗余 GetProperty 反射 fallback（缓存已覆盖全部可访问字段）
            if (AccessorCache.TryGetValue(fieldName, out var entry)
                && entry.Property.PropertyType == typeof(string))
            {
                return NormalizeString(entry.Accessor(_config) as string);
            }

            return null;
        }

        private static string? NormalizeString(string? value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static string? GetContainerFieldValue(ContainerConfig? container, string fieldName)
        {
            if (container == null) return null;

            return fieldName.ToLowerInvariant() switch
            {
                "sourcetype" => container.SourceType,
                "defaultconfig" => container.DefaultConfig?.ToString(),
                "autoload" => container.AutoLoad.ToString(),
                _ => null
            };
        }

        private static string? GetLayoutPositionFieldValue(LayoutPosition? layout, string fieldName)
        {
            if (layout == null) return null;

            return fieldName.ToLowerInvariant() switch
            {
                "left" => layout.Left.ToString(),
                "top" => layout.Top.ToString(),
                "width" => layout.Width.ToString(),
                "height" => layout.Height.ToString(),
                _ => null
            };
        }

        private static bool HasContainerField(ContainerConfig? container, string fieldName)
        {
            if (container == null) return false;

            return fieldName.ToLowerInvariant() switch
            {
                "sourcetype" => container.SourceType != null,
                "defaultconfig" => container.DefaultConfig != null,
                "autoload" => container.AutoLoad,
                _ => false
            };
        }

        private static bool HasLayoutPositionField(LayoutPosition? layout, string fieldName)
        {
            if (layout == null) return false;

            return fieldName.ToLowerInvariant() switch
            {
                "left" => true,
                "top" => true,
                "width" => true,
                "height" => true,
                _ => false
            };
        }
    }
}
