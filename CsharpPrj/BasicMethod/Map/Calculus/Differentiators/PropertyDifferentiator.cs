using System.Reflection;

namespace BasicMethod.Calculus.Differentiators
{
    public class PropertyDifferentiator<T> : DifferentiatorBase<T>
    {
        private readonly Func<T, T> _transform;

        public PropertyDifferentiator(string name, Func<PropertyInfo, bool> propertyFilter, Func<object?, object?> transform)
            : this(name, input => DifferentiateByReflection(input, propertyFilter, transform)) { }

        public PropertyDifferentiator(string name, Func<T, T> transform)
            : base(name)
        {
            _transform = transform ?? throw new ArgumentNullException(nameof(transform));
        }

        public override T Differentiate(T input)
        {
            return _transform(input);
        }

        private static T DifferentiateByReflection(T input, Func<PropertyInfo, bool> propertyFilter, Func<object?, object?> transform)
        {
            if (typeof(T).IsValueType)
            {
                return input;
            }

            return ReflectionHelper.TransformProperties(input, propertyFilter, transform);
        }
    }
}