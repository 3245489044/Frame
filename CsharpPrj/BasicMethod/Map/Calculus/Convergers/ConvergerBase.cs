namespace BasicMethod.Calculus.Convergers
{
    public abstract class ConvergerBase<T> : OperatorBase<T>, IConverger<T>
    {
        protected ConvergerBase(string name) : base(name) { }

        public abstract T Converge(T input);

        public abstract bool HasConverged(T current, T previous);
    }
}