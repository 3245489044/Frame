namespace BasicMethod.Calculus.Evolvers
{
    public class ConditionalEvolver<T> : EvolverBase<T>
    {
        private readonly List<(Func<T, bool> Condition, Func<T, T> Transform)> _conditions;
        private readonly Func<T, T> _default;

        public ConditionalEvolver(string name, Func<T, T> @default)
            : base(name)
        {
            _conditions = new List<(Func<T, bool>, Func<T, T>)>();
            _default = @default ?? throw new ArgumentNullException(nameof(@default));
        }

        public ConditionalEvolver<T> When(Func<T, bool> condition, Func<T, T> transform)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (transform == null) throw new ArgumentNullException(nameof(transform));

            _conditions.Add((condition, transform));
            return this;
        }

        public override T Evolve(T input)
        {
            foreach (var (condition, transform) in _conditions)
            {
                if (condition(input))
                    return transform(input);
            }
            return _default(input);
        }
    }
}