
#nullable enable
using System;
using System.Collections.Generic;

namespace BasicMethod.Declarative.Abstractions
{
    public abstract class RelationBase : IRelation
    {
        public string Name { get; }

        public IReadOnlyList<string> Attributes { get; }

        public abstract IEnumerable<IReadOnlyDictionary<string, object?>> Tuples { get; }

        protected RelationBase(string name, IReadOnlyList<string> attributes)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        }

        public override string ToString()
            => $"Relation: {Name}({string.Join(", ", Attributes)})";
    }
}
