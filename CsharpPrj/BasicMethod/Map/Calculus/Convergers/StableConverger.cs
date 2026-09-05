namespace BasicMethod.Calculus.Convergers
{
    public class StableConverger<T> : ConvergerBase<T>
        where T : IEquatable<T>
    {
        public StableConverger(string name) : base(name) { }

        public override T Converge(T input) => input;

        public override bool HasConverged(T current, T previous)
        {
            return current?.Equals(previous) ?? previous is null;
        }
    }
}