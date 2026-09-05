using BasicMethod.Calculus.Evolvers;

namespace BasicMethod.Calculus.Extensions
{
    public static class EvolverExtensions
    {
        public static IEvolver<T> Then<T>(
            this IEvolver<T> first,
            IEvolver<T> second)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));

            return new ComposedEvolver<T>(first, second);
        }

        private class ComposedEvolver<T> : EvolverBase<T>
        {
            private readonly IEvolver<T> _first;
            private readonly IEvolver<T> _second;

            public ComposedEvolver(IEvolver<T> first, IEvolver<T> second)
                : base($"{first.Name} ∘ {second.Name}")
            {
                _first = first;
                _second = second;
            }

            public override T Evolve(T input)
            {
                return _second.Evolve(_first.Evolve(input));
            }
        }
    }
}