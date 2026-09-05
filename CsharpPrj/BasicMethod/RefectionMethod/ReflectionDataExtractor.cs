#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Modules.ReflectionCommon;
using Modules.ReflectionCommon.Caching;

namespace Modules.ReflectionMethod
{
    #region 函数描述符模型 (从 FileDrivenFramework.Core.FunctionRegistry 迁移)

    public class FunctionDescriptor
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Type ReturnType { get; set; } = typeof(void);
        public bool IsAsync { get; set; }
        public List<ParameterDescriptor> Parameters { get; set; } = new();
        public Delegate? Delegate { get; set; }
        public Func<object[], Task<object>>? CompiledExecutor { get; set; }
    }

    public class ParameterDescriptor
    {
        public string Name { get; set; } = string.Empty;
        public Type Type { get; set; } = typeof(object);
        public bool IsOptional { get; set; }
        public object? DefaultValue { get; set; }
    }

    #endregion

    public class ReflectionDataExtractor : ReflectionCommon.IDataExtractor
    {
        public ReflectionDataExtractor()
        {
        }

        public T? GetFieldValue<T>(object target, string fieldName)
        {
            _ = target ?? throw new ArgumentNullException(nameof(target));

            var type = target.GetType();
            var value = GetFieldValueInternal(target, type, fieldName);

            return value is T ? (T)value : default;
        }

        private object? GetFieldValueInternal(object target, Type type, string fieldName)
        {
            if (target.TryGetFieldValue(fieldName, out var fieldValue))
                return fieldValue;

            if (target.TryGetPropertyValue(fieldName, out var propValue))
                return propValue;

            if (type.BaseType != null)
                return GetFieldValueInternal(target, type.BaseType, fieldName);

            return null;
        }

        public Dictionary<string, object?> ExtractAllFields(object target, bool includePrivate = true)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            var result = new Dictionary<string, object?>();
            ExtractFieldsRecursive(target, target.GetType(), result, includePrivate, new HashSet<object>());
            return result;
        }

        private void ExtractFieldsRecursive(object target, Type type, Dictionary<string, object?> result,
            bool includePrivate, HashSet<object> visited)
        {
            if (!type.IsValueType)
            {
                if (visited.Contains(target))
                    return;
                visited.Add(target);
            }

            var fields = ReflectionCommon.TypeExtensions.GetAllFields(type, includePrivate);
            foreach (var field in fields)
            {
                try
                {
                    var value = field.GetValue(target);
                    result[field.Name] = ResolveValue(value, visited);
                }
                catch { }
            }

            var properties = ReflectionCommon.TypeExtensions.GetAllProperties(type, includePrivate);
            foreach (var property in properties)
            {
                try
                {
                    if (property.CanRead)
                    {
                        var value = property.GetValue(target);
                        result[property.Name] = ResolveValue(value, visited);
                    }
                }
                catch { }
            }
        }

        private object? ResolveValue(object? value, HashSet<object> visited)
        {
            if (value == null)
                return null;

            var type = value.GetType();

            if (type.IsPrimitive || type == typeof(string) || type.IsEnum)
                return value;

            if (value is Rectangle rect)
                return string.Format(ReflectionConfig.FormatStrings.RectangleFormat, rect.X, rect.Y, rect.Width, rect.Height);

            if (value is Point point)
                return string.Format(ReflectionConfig.FormatStrings.PointFormat, point.X, point.Y);

            if (value is Size size)
                return string.Format(ReflectionConfig.FormatStrings.SizeFormat, size.Width, size.Height);

            if (value is System.Collections.IDictionary dict)
            {
                var items = new List<string>();
                int count = 0;
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    if (count >= ReflectionConfig.NumericConstants.MaxDictionaryKeys)
                        break;
                    items.Add(entry.Key + "=" + entry.Value);
                    count++;
                }
                return string.Format(ReflectionConfig.FormatStrings.DictionaryFormat, string.Join(",", items));
            }

            if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                int count = 0;
                var enumerator = enumerable.GetEnumerator();
                using (enumerator as IDisposable)
                {
                    while (enumerator.MoveNext())
                        count++;
                }
                return string.Format(ReflectionConfig.FormatStrings.CollectionFormat, count);
            }

            return string.Format(ReflectionConfig.FormatStrings.TypeNameFormat, type.Name);
        }

        public T? ExtractTypedValue<T>(object target, string path)
        {
            var parts = path.Split(ReflectionConfig.Separators.Dot);
            object? current = target;

            foreach (var part in parts)
            {
                if (current == null)
                    return default;

                current = GetFieldValueInternal(current, current.GetType(), part);
            }

            return current is T ? (T)current : default;
        }

        public Dictionary<string, object?> ExtractByPattern(object target, string pattern)
        {
            var result = new Dictionary<string, object?>();
            var allFields = ExtractAllFields(target);

            foreach (var kvp in allFields)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(kvp.Key, pattern))
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            return result;
        }
    }

}
