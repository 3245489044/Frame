
namespace BasicMethod.Functor
{
    public class FunctorBuilder
    {
        private readonly string _name;
        private readonly Dictionary<string, object?> _initialValues = new();

        public FunctorBuilder(string name)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public FunctorBuilder With<T>(T value) where T : class
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            _initialValues[typeof(T).FullName!] = value;
            return this;
        }

        public FunctorBuilder With(string key, object? value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            _initialValues[key] = value;
            return this;
        }

        public FunctorBuilder WithAll(params (string Key, object? Value)[] items)
        {
            foreach (var (key, value) in items)
                _initialValues[key] = value;
            return this;
        }

        public IFunctorScope Build()
        {
            return FunctorScope.Begin(_name, functor =>
            {
                foreach (var kvp in _initialValues)
                    functor.Set(kvp.Key, kvp.Value);
            });
        }
    }

    public static class Functor
    {
        public static FunctorBuilder Create(string name)
            => new FunctorBuilder(name);

        public static IFunctorScope Begin(string name)
            => FunctorScope.Begin(name);

        public static IFunctorScope Begin(string name, Action<IFunctor> initialize)
            => FunctorScope.Begin(name, initialize);

        public static IFunctorScope? Current => FunctorScope.Current;
    }
}
