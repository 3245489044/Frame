
#nullable enable
using System;

namespace BasicMethod.Declarative.Mappers
{
    public abstract class DeclarativeMapper<TDeclaration, TRuntime>
        : IDeclarativeMapper<TDeclaration, TRuntime>
        where TDeclaration : IDeclaration
    {
        public string Name { get; }

        public string Description { get; }

        protected DeclarativeMapper(string name, string description)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }

        public abstract TRuntime Map(TDeclaration declaration);

        public override string ToString() => $"Mapper: {Name} — {Description}";
    }
}
