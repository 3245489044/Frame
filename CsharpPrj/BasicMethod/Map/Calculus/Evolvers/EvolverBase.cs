namespace BasicMethod.Calculus.Evolvers
{
    public abstract class EvolverBase<T> : OperatorBase<T>, IEvolver<T>
    {
        protected EvolverBase(string name) : base(name) { }

        public abstract T Evolve(T input);
    }
}