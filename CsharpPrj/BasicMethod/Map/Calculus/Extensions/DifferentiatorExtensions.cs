using BasicMethod.Calculus.Differentiators;

namespace BasicMethod.Calculus.Extensions
{
    public static class DifferentiatorExtensions
    {
        public static IDifferentiator<T> Then<T>(
            this IDifferentiator<T> first,
            IDifferentiator<T> second)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));

            return new ComposedDifferentiator<T>(first, second);
        }

        private class ComposedDifferentiator<T> : DifferentiatorBase<T>
        {
            private readonly IDifferentiator<T> _first;
            private readonly IDifferentiator<T> _second;

            public ComposedDifferentiator(IDifferentiator<T> first, IDifferentiator<T> second)
                : base($"{first.Name} ∘ {second.Name}")
            {
                _first = first;
                _second = second;
            }

            public override T Differentiate(T input)
            {
                return _second.Differentiate(_first.Differentiate(input));
            }
        }
    }
}