
namespace BasicMethod.Functor
{
    public interface IFunctor
    {
        string Name { get; }

        T? Get<T>() where T : class;

        void Set<T>(T value) where T : class;

        object? Get(string key);

        void Set(string key, object? value);

        bool Has<T>() where T : class;

        bool Has(string key);

        bool Remove<T>() where T : class;

        bool Remove(string key);
    }

    public interface IFunctorScope : IFunctor, IDisposable
    {
        IFunctorScope? Parent { get; }

        IFunctorScope CreateScope();

        IReadOnlyDictionary<string, object?> Snapshot();
    }

    public interface IFunctorFactory
    {
        IFunctorScope BeginScope(string name);

        IFunctorScope BeginScope(string name, Action<IFunctor> initialize);
    }
}
