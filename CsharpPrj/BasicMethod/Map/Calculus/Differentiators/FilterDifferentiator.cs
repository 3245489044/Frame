namespace BasicMethod.Calculus.Differentiators
{
    public class FilterDifferentiator<T> : DifferentiatorBase<IEnumerable<T>>
    {
        private readonly Func<T, bool> _filter;
        private readonly Func<T, T> _transform;

        public FilterDifferentiator(string name, Func<T, bool> filter, Func<T, T> transform)
            : base(name)
        {
            _filter = filter ?? throw new ArgumentNullException(nameof(filter));
            _transform = transform ?? throw new ArgumentNullException(nameof(transform));
        }

        public override IEnumerable<T> Differentiate(IEnumerable<T> input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            return input.Select(item => _filter(item) ? _transform(item) : item);
        }
    }
}