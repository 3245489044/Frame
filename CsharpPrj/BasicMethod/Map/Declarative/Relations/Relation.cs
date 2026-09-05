
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BasicMethod.Declarative.Abstractions;

namespace BasicMethod.Declarative.Relations
{
    public class Relation : RelationBase
    {
        private readonly IEnumerable<IReadOnlyDictionary<string, object?>> _tuples;

        public override IEnumerable<IReadOnlyDictionary<string, object?>> Tuples => _tuples;

        public Relation(
            string name,
            IReadOnlyList<string> attributes,
            IEnumerable<IReadOnlyDictionary<string, object?>> tuples)
            : base(name, attributes)
        {
            _tuples = tuples ?? throw new ArgumentNullException(nameof(tuples));
        }

        public static Relation FromTuples(
            string name,
            IEnumerable<IReadOnlyDictionary<string, object?>> tuples)
        {
            var tupleList = tuples?.ToList()
                ?? throw new ArgumentNullException(nameof(tuples));

            var firstTuple = tupleList.FirstOrDefault(t => t != null && t.Count > 0);
            IReadOnlyList<string> attributes = firstTuple != null
                ? firstTuple.Keys.ToList().AsReadOnly()
                : Array.Empty<string>();

            return new Relation(name, attributes, tupleList);
        }

        public static Relation Empty(string name, IReadOnlyList<string> attributes)
            => new(name, attributes, Enumerable.Empty<IReadOnlyDictionary<string, object?>>());
    }
}
