
using System.Reflection;
using Share.LogicComposition.Models;

namespace Share.LogicComposition.DefaultImplementations
{
    public static class ObjectMerger
    {
        public static void Merge<T>(T target, T source, MergeOptions? options = null)
            where T : class
        {
            MergeWithConflicts(target, source, options, null);
        }

        public static void MergeWithConflicts<T>(T target, T source, MergeOptions? options = null,
            List<MergeConflict>? conflicts = null)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(source);

            options ??= MergeOptions.Default;

            if (options.IncludeProperties)
                MergeProperties(target, source, options, conflicts);

            if (options.IncludeFields)
                MergeFields(target, source, options, conflicts);
        }

        public static T MergeToNew<T>(T source, MergeOptions? options = null)
            where T : class, new()
        {
            var target = new T();
            Merge(target, source, options ?? MergeOptions.Overwrite);
            return target;
        }

        public static T Merge<T>(T target, T source, params string[] skipPropertyNames)
            where T : class
        {
            var options = new MergeOptions
            {
                Strategy = MergeStrategy.SkipNull,
                SkipNames = skipPropertyNames.Length > 0 ? new HashSet<string>(skipPropertyNames) : null
            };
            Merge(target, source, options);
            return target;
        }

        public static void MergeAll<T>(T target, params T[] sources)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(target);

            foreach (var source in sources)
            {
                if (source != null)
                    Merge(target, source);
            }
        }

        public static void MergeAll<T>(T target, MergeOptions options, params T[] sources)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(target);

            foreach (var source in sources)
            {
                if (source != null)
                    Merge(target, source, options);
            }
        }

        #region 内部实现

        private static void MergeProperties<T>(T target, T source, MergeOptions options,
            List<MergeConflict>? conflicts = null)
            where T : class
        {
            foreach (var prop in typeof(T).GetProperties(options.BindingFlags))
            {
                if (!prop.CanWrite || !prop.CanRead)
                    continue;

                if (options.SkipNames?.Contains(prop.Name) == true)
                    continue;

                var sourceValue = prop.GetValue(source);
                var targetValue = prop.GetValue(target);

                if (ShouldMerge(sourceValue, prop.PropertyType, options))
                {
                    if (conflicts != null && targetValue != null && !Equals(targetValue, sourceValue))
                    {
                        conflicts.Add(new MergeConflict(
                            prop.Name, sourceValue, targetValue, "Overwritten"));
                    }
                    prop.SetValue(target, sourceValue);
                }
            }
        }

        private static void MergeFields<T>(T target, T source, MergeOptions options,
            List<MergeConflict>? conflicts = null)
            where T : class
        {
            foreach (var field in typeof(T).GetFields(options.BindingFlags))
            {
                if (options.SkipNames?.Contains(field.Name) == true)
                    continue;

                var sourceValue = field.GetValue(source);
                var targetValue = field.GetValue(target);

                if (ShouldMerge(sourceValue, field.FieldType, options))
                {
                    if (conflicts != null && targetValue != null && !Equals(targetValue, sourceValue))
                    {
                        conflicts.Add(new MergeConflict(
                            field.Name, sourceValue, targetValue, "Overwritten"));
                    }
                    field.SetValue(target, sourceValue);
                }
            }
        }

        private static bool ShouldMerge(object? sourceValue, Type valueType, MergeOptions options)
        {
            return options.Strategy switch
            {
                MergeStrategy.Overwrite => true,
                MergeStrategy.FillDefaults => IsDefaultValue(sourceValue, valueType),
                MergeStrategy.SkipNullWithEmptyString => sourceValue != null,
                MergeStrategy.SkipNull => sourceValue != null && !IsEmptyString(sourceValue),
                _ => sourceValue != null
            };
        }

        private static bool IsEmptyString(object? value) =>
            value is string str && string.IsNullOrEmpty(str);

        private static bool IsDefaultValue(object? value, Type type)
        {
            if (value == null) return true;

            if (type == typeof(string))
                return string.IsNullOrEmpty((string)value);

            if (type.IsValueType)
            {
                var defaultValue = Activator.CreateInstance(type);
                return value.Equals(defaultValue);
            }

            return false;
        }

        #endregion
    }
}
