namespace BasicMethod.Calculus.Integrators
{
    public class UnionIntegrator<T> : IntegratorBase<IEnumerable<T>>
    {
        public UnionIntegrator(string name) : base(name) { }

        public override IEnumerable<T> Integrate(IEnumerable<T> current, IEnumerable<T> delta)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (delta == null) throw new ArgumentNullException(nameof(delta));

            return current.Concat(delta).Distinct();
        }

        public override IEnumerable<T> Reset() => Enumerable.Empty<T>();
    }
}