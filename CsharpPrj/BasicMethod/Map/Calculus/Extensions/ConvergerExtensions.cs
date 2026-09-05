using BasicMethod.Calculus.Convergers;

namespace BasicMethod.Calculus.Extensions
{
    public static class ConvergerExtensions
    {
        public static IConverger<T> Then<T>(
            this IConverger<T> first,
            IConverger<T> second)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));

            return new ComposedConverger<T>(first, second);
        }

        private class ComposedConverger<T> : ConvergerBase<T>
        {
            private readonly IConverger<T> _first;
            private readonly IConverger<T> _second;

            public ComposedConverger(IConverger<T> first, IConverger<T> second)
                : base($"{first.Name} ∘ {second.Name}")
            {
                _first = first;
                _second = second;
            }

            public override T Converge(T input)
            {
                return _second.Converge(_first.Converge(input));
            }

            public override bool HasConverged(T current, T previous)
            {
                return _first.HasConverged(current, previous) && _second.HasConverged(current, previous);
            }
        }
    }
}