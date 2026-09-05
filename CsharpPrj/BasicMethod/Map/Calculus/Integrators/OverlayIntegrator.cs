using System.Reflection;

namespace BasicMethod.Calculus.Integrators
{
    public class OverlayIntegrator<T> : IntegratorBase<T>
    {
        private readonly Func<T> _factory;
        private readonly Func<T, T, T> _overlayFunc;

        public OverlayIntegrator(string name, Func<PropertyInfo, bool> overlayFilter)
            : this(name, () => default!, overlayFilter, (current, delta) => OverlayByReflection(current, delta, overlayFilter)) { }

        public OverlayIntegrator(string name, Func<T> factory, Func<PropertyInfo, bool> overlayFilter, Func<T, T, T> overlayFunc)
            : base(name)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _overlayFunc = overlayFunc ?? throw new ArgumentNullException(nameof(overlayFunc));
        }

        public override T Integrate(T current, T delta)
        {
            return _overlayFunc(current, delta);
        }

        public override T Reset() => _factory();

        private static T OverlayByReflection(T current, T delta, Func<PropertyInfo, bool> overlayFilter)
        {
            if (typeof(T).IsValueType)
            {
                return !EqualityComparer<T>.Default.Equals(delta, default) ? delta : current;
            }

            return ReflectionHelper.MergeProperties(current, delta, overlayFilter);
        }
    }
}