
#nullable enable

namespace Modules.GraphProcessing.Normalization
{
    public readonly struct NodeLocation : IEquatable<NodeLocation>
    {
        public int ChainIndex { get; }

        public int Position { get; }

        public NodeLocation(int chainIndex, int position)
        {
            ChainIndex = chainIndex;
            Position = position;
        }

        public bool Equals(NodeLocation other)
            => ChainIndex == other.ChainIndex && Position == other.Position;

        public override bool Equals(object? obj)
            => obj is NodeLocation other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(ChainIndex, Position);

        public override string ToString()
            => $"[Chain:{ChainIndex}, Pos:{Position}]";
    }

    public class FullRenameContext
    {
        public string OriginalName { get; }

        public string NewName { get; set; }

        public NodeLocation Location { get; }

        public bool IsRenamed => !string.Equals(OriginalName, NewName, StringComparison.Ordinal);

        public FullRenameContext(string originalName, string newName, NodeLocation location)
        {
            OriginalName = originalName;
            NewName = newName;
            Location = location;
        }

        public KeyValuePair<string, string> ToMapping()
            => new(OriginalName, NewName);

        public override string ToString()
            => IsRenamed
                ? $"Rename: '{OriginalName}' -> '{NewName}' at {Location}"
                : $"No rename: '{OriginalName}' at {Location}";
    }

    public class RenamePlan
    {
        public IReadOnlyList<FullRenameContext> RenameContexts { get; }

        public IReadOnlyDictionary<string, string> NameMapping { get; }

        public bool HasRenames => RenameContexts.Any(c => c.IsRenamed);

        public RenamePlan(
            IReadOnlyList<FullRenameContext> renameContexts,
            IReadOnlyDictionary<string, string> nameMapping)
        {
            RenameContexts = renameContexts;
            NameMapping = nameMapping;
        }
    }

    public class NormalizationPlanner
    {
        private readonly IdentifierExtractor _extractor;
        private readonly string _renamePattern;

        public NormalizationPlanner(string renamePattern = "{0}_chain{1}_dup{2}", string separator = "_")
        {
            _renamePattern = renamePattern;
            _extractor = new IdentifierExtractor(separator);
        }

        public RenamePlan Plan(IReadOnlyList<string> names, int chainIndex)
        {
            var allContexts = new List<FullRenameContext>();
            var globalMapping = new Dictionary<string, string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicateCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int pos = 0; pos < names.Count; pos++)
            {
                var name = names[pos];
                var normalized = _extractor.Extract(name);

                if (seen.Add(normalized))
                {
                    allContexts.Add(new FullRenameContext(name, name, new NodeLocation(chainIndex, pos)));
                    globalMapping[name] = name;
                }
                else
                {
                    if (!duplicateCount.ContainsKey(normalized))
                        duplicateCount[normalized] = 1;

                    var count = duplicateCount[normalized]++;
                    var newName = string.Format(_renamePattern, normalized, chainIndex, count);

                    allContexts.Add(new FullRenameContext(name, newName, new NodeLocation(chainIndex, pos)));
                    globalMapping[name] = newName;
                }
            }

            return new RenamePlan(allContexts.AsReadOnly(), globalMapping.AsReadOnly());
        }


        public RenamePlan PlanCrossChain(IReadOnlyList<IReadOnlyList<string>> chainNames)
        {
            var allContexts = new List<FullRenameContext>();
            var globalMapping = new Dictionary<string, string>();
            var globalSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicateCount = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);

            for (int chainIdx = 0; chainIdx < chainNames.Count; chainIdx++)
            {
                var names = chainNames[chainIdx];
                for (int pos = 0; pos < names.Count; pos++)
                {
                    var name = names[pos];
                    var normalized = _extractor.Extract(name);

                    if (globalSeen.Add(normalized))
                    {
                        allContexts.Add(new FullRenameContext(name, name, new NodeLocation(chainIdx, pos)));
                        globalMapping[name] = name;
                    }
                    else
                    {
                        if (!duplicateCount.TryGetValue(normalized, out var chainCounts))
                        {
                            chainCounts = new Dictionary<int, int>();
                            duplicateCount[normalized] = chainCounts;
                        }

                        if (!chainCounts.TryGetValue(chainIdx, out var count))
                            count = 1;
                        else
                            count++;

                        chainCounts[chainIdx] = count;
                        var newName = string.Format(_renamePattern, normalized, chainIdx, count);

                        allContexts.Add(new FullRenameContext(name, newName, new NodeLocation(chainIdx, pos)));
                        globalMapping[name] = newName;
                    }
                }
            }

            return new RenamePlan(allContexts.AsReadOnly(), globalMapping.AsReadOnly());
        }

    }
}
