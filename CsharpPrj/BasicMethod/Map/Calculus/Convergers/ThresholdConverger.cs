namespace BasicMethod.Calculus.Convergers
{
    public class ThresholdConverger<T, TDistance> : ConvergerBase<T>
        where TDistance : IComparable<TDistance>
    {
        private readonly Func<T, T, TDistance> _distance;
        private readonly TDistance _threshold;

        public ThresholdConverger(string name, Func<T, T, TDistance> distance, TDistance threshold)
            : base(name)
        {
            _distance = distance ?? throw new ArgumentNullException(nameof(distance));
            _threshold = threshold;
        }

        public override T Converge(T input) => input;

        public override bool HasConverged(T current, T previous)
        {
            if (current == null && previous == null) return true;
            if (current == null || previous == null) return false;

            return _distance(current, previous).CompareTo(_threshold) <= 0;
        }
    }
}