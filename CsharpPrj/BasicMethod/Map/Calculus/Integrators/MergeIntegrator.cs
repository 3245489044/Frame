using System.Reflection;

namespace BasicMethod.Calculus.Integrators
{
    public class MergeIntegrator<T> : IntegratorBase<T>
    {
        private readonly Func<T> _factory;
        private readonly Func<T, T, T> _mergeFunc;

        public MergeIntegrator(string name)
            : this(name, () => default!, (current, delta) => MergeByReflection(current, delta)) { }

        public MergeIntegrator(string name, Func<T> factory, Func<T, T, T> mergeFunc)
            : base(name)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _mergeFunc = mergeFunc ?? throw new ArgumentNullException(nameof(mergeFunc));
        }

        public override T Integrate(T current, T delta)
        {
            return _mergeFunc(current, delta);
        }

        public override T Reset() => _factory();

        private static T MergeByReflection(T current, T delta)
        {
            if (typeof(T).IsValueType)
            {
                return !EqualityComparer<T>.Default.Equals(delta, default) ? delta : current;
            }

            return ReflectionHelper.MergeProperties(current, delta);
        }
    }
}