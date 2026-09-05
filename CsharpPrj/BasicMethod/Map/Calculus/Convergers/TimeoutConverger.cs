namespace BasicMethod.Calculus.Convergers
{
    public class TimeoutConverger<T> : ConvergerBase<T>
    {
        private readonly Func<T, T, bool> _convergenceCheck;
        private readonly TimeSpan _timeout;
        private DateTime? _startTime;

        public TimeoutConverger(string name, Func<T, T, bool> convergenceCheck, TimeSpan timeout)
            : base(name)
        {
            _convergenceCheck = convergenceCheck ?? throw new ArgumentNullException(nameof(convergenceCheck));
            _timeout = timeout;
        }

        public override T Converge(T input)
        {
            _startTime ??= DateTime.Now;
            return input;
        }

        public override bool HasConverged(T current, T previous)
        {
            if (_startTime == null)
                _startTime = DateTime.Now;

            if (DateTime.Now - _startTime.Value >= _timeout)
                return true;

            return _convergenceCheck(current, previous);
        }

        public void Reset() => _startTime = null;
    }
}