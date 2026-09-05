namespace BasicMethod.Calculus
{
    public abstract class OperatorBase<T>
    {
        public string Name { get; }

        protected OperatorBase(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }
}