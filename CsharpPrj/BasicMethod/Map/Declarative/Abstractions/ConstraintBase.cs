
#nullable enable
using System;

namespace BasicMethod.Declarative.Abstractions
{
    public abstract class ConstraintBase<T> : IConstraint<T>
    {
        public string Name { get; }

        public string Description { get; }

        protected ConstraintBase(string name, string description)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }

        public abstract ConstraintResult Check(T input);

        public override string ToString() => $"Constraint: {Name} — {Description}";
    }
}
