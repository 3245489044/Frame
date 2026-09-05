using BasicMethod.Calculus.Integrators;

namespace BasicMethod.Calculus.Extensions
{
    public static class IntegratorExtensions
    {
        public static IIntegrator<T> Then<T>(
            this IIntegrator<T> first,
            IIntegrator<T> second)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));

            return new ComposedIntegrator<T>(first, second);
        }

        private class ComposedIntegrator<T> : IntegratorBase<T>
        {
            private readonly IIntegrator<T> _first;
            private readonly IIntegrator<T> _second;

            public ComposedIntegrator(IIntegrator<T> first, IIntegrator<T> second)
                : base($"{first.Name} ∘ {second.Name}")
            {
                _first = first;
                _second = second;
            }

            public override T Integrate(T current, T delta)
            {
                var firstResult = _first.Integrate(current, delta);
                return _second.Integrate(current, firstResult);
            }

            public override T Reset() => _second.Reset();
        }
    }
}