
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BasicMethod.Declarative.Abstractions;

namespace BasicMethod.Declarative.Relations
{
    public class Project : RelationBase
    {
        private readonly IRelation _source;
        private readonly HashSet<string> _selectedAttributes;

        public override IEnumerable<IReadOnlyDictionary<string, object?>> Tuples
        {
            get
            {
                var seen = new HashSet<string>();
                foreach (var tuple in _source.Tuples)
                {
                    var projected = new Dictionary<string, object?>();
                    foreach (var attr in _selectedAttributes)
                    {
                        if (tuple.TryGetValue(attr, out var value))
                            projected[attr] = value;
                    }

                    var key = string.Join("|", projected.OrderBy(kvp => kvp.Key)
                        .Select(kvp => $"{kvp.Key}={kvp.Value}"));
                    if (seen.Add(key))
                        yield return projected;
                }
            }
        }

        public Project(
            string name,
            IRelation source,
            params string[] attributes)
            : base(name, attributes)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _selectedAttributes = new HashSet<string>(attributes
                ?? throw new ArgumentNullException(nameof(attributes)));
        }

    }
}
