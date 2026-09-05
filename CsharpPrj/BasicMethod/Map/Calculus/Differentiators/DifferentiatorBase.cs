namespace BasicMethod.Calculus.Differentiators
{
    public abstract class DifferentiatorBase<T> : OperatorBase<T>, IDifferentiator<T>
    {
        protected DifferentiatorBase(string name) : base(name) { }

        public abstract T Differentiate(T input);
    }
}