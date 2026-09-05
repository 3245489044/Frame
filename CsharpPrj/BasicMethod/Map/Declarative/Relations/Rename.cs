
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BasicMethod.Declarative.Abstractions;

namespace BasicMethod.Declarative.Relations
{
    public class Rename : RelationBase
    {
        private readonly IRelation _source;
        private readonly IReadOnlyDictionary<string, string> _renameMap;

        public override IEnumerable<IReadOnlyDictionary<string, object?>> Tuples
        {
            get
            {
                foreach (var tuple in _source.Tuples)
                {
                    var renamed = new Dictionary<string, object?>();
                    foreach (var kvp in tuple)
                    {
                        var newName = _renameMap.TryGetValue(kvp.Key, out var mapped)
                            ? mapped
                            : kvp.Key;
                        renamed[newName] = kvp.Value;
                    }
                    yield return renamed;
                }
            }
        }

        public Rename(
            string name,
            IRelation source,
            IReadOnlyDictionary<string, string> renameMap)
            : base(name, ComputeNewAttributes(source.Attributes, renameMap))
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _renameMap = renameMap ?? throw new ArgumentNullException(nameof(renameMap));
        }

        public Rename(
            string name,
            IRelation source,
            string oldName,
            string newName)
            : this(name, source, new Dictionary<string, string> { [oldName] = newName })
        {
        }

        private static IReadOnlyList<string> ComputeNewAttributes(
            IReadOnlyList<string> original,
            IReadOnlyDictionary<string, string> renameMap)
        {
            return original
                .Select(attr => renameMap.TryGetValue(attr, out var newName) ? newName : attr)
                .ToList()
                .AsReadOnly();
        }
    }
}
