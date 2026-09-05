namespace BasicMethod.Calculus
{
    public interface IDifferentiator<T>
    {
        string Name { get; }

        T Differentiate(T input);
    }

    public interface IIntegrator<T>
    {
        string Name { get; }

        T Integrate(T current, T delta);

        T Reset();
    }

    public interface IEvolver<T>
    {
        string Name { get; }

        T Evolve(T input);
    }

    public interface IConverger<T>
    {
        string Name { get; }

        T Converge(T input);

        bool HasConverged(T current, T previous);
    }
}
