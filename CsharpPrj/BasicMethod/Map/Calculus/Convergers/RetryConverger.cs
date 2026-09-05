namespace BasicMethod.Calculus.Convergers
{
    public class RetryConverger<T> : ConvergerBase<T>
    {
        private readonly Func<T, (bool Success, T Result)> _action;
        private readonly int _maxRetries;
        private int _attempts;

        public RetryConverger(string name, Func<T, (bool Success, T Result)> action, int maxRetries = 3)
            : base(name)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
            _maxRetries = maxRetries;
        }

        public override T Converge(T input)
        {
            _attempts++;
            var (success, result) = _action(input);
            return success ? result : input;
        }

        public override bool HasConverged(T current, T previous)
        {
            return EqualityComparer<T>.Default.Equals(current, previous) || _attempts >= _maxRetries;
        }

        public void Reset() => _attempts = 0;
    }
}