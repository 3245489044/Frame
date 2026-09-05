using System.Reflection;

namespace BasicMethod.Calculus
{
    public static class ReflectionHelper
    {
        public static T CopyProperties<T>(T source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var result = Activator.CreateInstance<T>()!;
            foreach (var prop in typeof(T).GetProperties())
            {
                var value = prop.GetValue(source);
                prop.SetValue(result, value);
            }
            return result;
        }

        public static T TransformProperties<T>(T source, Func<PropertyInfo, bool> propertyFilter, Func<object?, object?> transform)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (propertyFilter == null) throw new ArgumentNullException(nameof(propertyFilter));
            if (transform == null) throw new ArgumentNullException(nameof(transform));

            var result = Activator.CreateInstance<T>()!;
            foreach (var prop in typeof(T).GetProperties())
            {
                var value = prop.GetValue(source);
                if (propertyFilter(prop))
                    value = transform(value);
                prop.SetValue(result, value);
            }
            return result;
        }

        public static T MergeProperties<T>(T current, T delta, Func<PropertyInfo, bool>? overlayFilter = null)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (delta == null) throw new ArgumentNullException(nameof(delta));

            var result = Activator.CreateInstance<T>()!;
            foreach (var prop in typeof(T).GetProperties())
            {
                var currentValue = prop.GetValue(current);
                var deltaValue = prop.GetValue(delta);

                if (overlayFilter == null || overlayFilter(prop))
                    prop.SetValue(result, !EqualityComparer<object?>.Default.Equals(deltaValue, default) ? deltaValue : currentValue);
                else
                    prop.SetValue(result, currentValue);
            }
            return result;
        }
    }
}