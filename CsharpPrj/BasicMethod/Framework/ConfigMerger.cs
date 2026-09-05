
using Modules.Framework.Abstractions;

namespace Modules.Framework
{
    public class ConfigMerger<TConfig> : IConfigMerger<TConfig>
        where TConfig : class
    {
        public void Merge(TConfig target, TConfig source, bool overwrite = true)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var properties = typeof(TConfig).GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            foreach (var prop in properties)
            {
                if (!prop.CanRead || !prop.CanWrite)
                    continue;

                var sourceValue = prop.GetValue(source);
                if (sourceValue == null)
                    continue;

                if (IsDefaultValue(sourceValue, prop.PropertyType))
                    continue;

                var targetValue = prop.GetValue(target);

                if (!overwrite)
                {
                    if (targetValue != null && !IsDefaultValue(targetValue, prop.PropertyType))
                        continue;
                }

                if (targetValue == null || IsDefaultValue(targetValue, prop.PropertyType))
                {
                    prop.SetValue(target, sourceValue);
                    continue;
                }

                if (sourceValue is System.Collections.IList sourceList
                    && targetValue is System.Collections.IList targetList)
                {
                    foreach (var item in sourceList)
                    {
                        if (!targetList.Contains(item))
                        {
                            targetList.Add(item);
                        }
                    }
                    continue;
                }

                if (sourceValue is System.Collections.IDictionary sourceDict
                    && targetValue is System.Collections.IDictionary targetDict)
                {
                    foreach (var key in sourceDict.Keys)
                    {
                        targetDict[key] = sourceDict[key];
                    }
                    continue;
                }

                prop.SetValue(target, sourceValue);
            }
        }

        private static bool IsDefaultValue(object value, Type type)
        {
            if (type == typeof(string))
                return string.IsNullOrEmpty((string)value);

            if (type == typeof(int))
                return (int)value == 0;

            if (type == typeof(long))
                return (long)value == 0;

            if (type == typeof(double))
                return (double)value == 0;

            if (type == typeof(float))
                return (float)value == 0;

            if (type == typeof(bool))
                return (bool)value == false;

            if (type.IsValueType)
            {
                var defaultValue = Activator.CreateInstance(type);
                return value.Equals(defaultValue);
            }

            return value == null;
        }
    }
}
