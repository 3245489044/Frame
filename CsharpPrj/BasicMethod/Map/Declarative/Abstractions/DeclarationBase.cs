
#nullable enable
using System;

namespace BasicMethod.Declarative.Abstractions
{
    public abstract class DeclarationBase : IDeclaration
    {
        public string Name { get; }

        public string Description { get; }

        public string DeclarationType { get; }

        protected DeclarationBase(string name, string description, string declarationType)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            DeclarationType = declarationType ?? throw new ArgumentNullException(nameof(declarationType));
        }

        public override string ToString() => $"[{DeclarationType}] {Name}: {Description}";
    }

    public abstract class DeclarationBase<TTarget> : DeclarationBase, IDeclaration<TTarget>
    {
        public TTarget? Target { get; protected set; }

        protected DeclarationBase(string name, string description, string declarationType, TTarget? target = default)
            : base(name, description, declarationType)
        {
            Target = target;
        }
    }
}
