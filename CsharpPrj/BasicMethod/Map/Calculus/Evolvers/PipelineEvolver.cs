namespace BasicMethod.Calculus.Evolvers
{
    public class PipelineEvolver<T> : EvolverBase<T>
    {
        private readonly List<IEvolver<T>> _pipeline;

        public PipelineEvolver(string name, params IEvolver<T>[] pipeline)
            : base(name)
        {
            _pipeline = pipeline?.ToList() ?? new List<IEvolver<T>>();
        }

        public PipelineEvolver(string name, params Func<T, T>[] steps)
            : base(name)
        {
            if (steps == null)
            {
                _pipeline = new List<IEvolver<T>>();
            }
            else
            {
                _pipeline = steps.Select(s => new FuncEvolver<T>(s)).Cast<IEvolver<T>>().ToList();
            }
        }

        public override T Evolve(T input)
        {
            var result = input;
            foreach (var evolver in _pipeline)
            {
                result = evolver.Evolve(result);
            }
            return result;
        }

        private class FuncEvolver<TValue> : EvolverBase<TValue>
        {
            private readonly Func<TValue, TValue> _func;

            public FuncEvolver(Func<TValue, TValue> func)
                : base("FuncEvolver")
            {
                _func = func ?? throw new ArgumentNullException(nameof(func));
            }

            public override TValue Evolve(TValue input) => _func(input);
        }
    }
}