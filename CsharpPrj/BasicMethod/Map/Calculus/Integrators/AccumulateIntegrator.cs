namespace BasicMethod.Calculus.Integrators
{
    public class AccumulateIntegrator<T> : IntegratorBase<List<T>>
    {
        public AccumulateIntegrator(string name) : base(name) { }

        public override List<T> Integrate(List<T> current, List<T> delta)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (delta == null) throw new ArgumentNullException(nameof(delta));

            var result = new List<T>(current);
            result.AddRange(delta);
            return result;
        }

        public override List<T> Reset() => new List<T>();
    }
}