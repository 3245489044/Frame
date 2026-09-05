namespace BasicMethod.Calculus.Integrators
{
    public abstract class IntegratorBase<T> : OperatorBase<T>, IIntegrator<T>
    {
        protected IntegratorBase(string name) : base(name) { }

        public abstract T Integrate(T current, T delta);

        public abstract T Reset();
    }
}