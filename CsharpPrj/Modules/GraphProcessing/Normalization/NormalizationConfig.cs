
#nullable enable

using Modules.Transformers;
using GraphFoundation.Topology.Transformers;

namespace Modules.GraphProcessing.Normalization
{
    public interface INormalizationConfig
    {
        bool RemoveIsolatedNodes { get; }

        bool NormalizeIdentifiers { get; }

        bool MergeDuplicateEdges { get; }

        bool ValidateIntegrity { get; }

        bool VerboseLogging { get; }

        IMatchStrategy<Connection, object>? DedupeMatchStrategy { get; }

        IReduceStrategy<Connection>? DedupeReduceStrategy { get; }
    }

    public class NormalizationConfig : INormalizationConfig
    {
        public bool RemoveIsolatedNodes { get; set; } = true;

        public bool NormalizeIdentifiers { get; set; } = false;

        public bool MergeDuplicateEdges { get; set; } = true;

        public bool ValidateIntegrity { get; set; } = true;

        public bool VerboseLogging { get; set; } = false;

        public IMatchStrategy<Connection, object>? DedupeMatchStrategy { get; set; }

        public IReduceStrategy<Connection>? DedupeReduceStrategy { get; set; }

        public static NormalizationConfig CreateBasic()
            => new()
            {
                MergeDuplicateEdges = true,
                DedupeMatchStrategy = new SourceTargetDirectionMatch().AsObjectMatch(),
                DedupeReduceStrategy = new KeepFirstReduce(),
                RemoveIsolatedNodes = false,
                ValidateIntegrity = false
            };

        public static NormalizationConfig CreateWeightAware()
            => new()
            {
                MergeDuplicateEdges = true,
                DedupeMatchStrategy = new SourceTargetDirectionMatch().AsObjectMatch(),
                DedupeReduceStrategy = new KeepHigherWeightReduce(),
                RemoveIsolatedNodes = true,
                ValidateIntegrity = true
            };

        public static NormalizationConfig CreateStrict()
            => new()
            {
                MergeDuplicateEdges = true,
                DedupeMatchStrategy = new SourceTargetDirectionMatch().AsObjectMatch(),
                DedupeReduceStrategy = new KeepFirstReduce(),
                RemoveIsolatedNodes = false,
                ValidateIntegrity = true
            };

        public static NormalizationConfig CreateUndirected()
            => new()
            {
                MergeDuplicateEdges = true,
                DedupeMatchStrategy = new UndirectedMatch().AsObjectMatch(),
                DedupeReduceStrategy = new KeepHigherWeightReduce(),
                RemoveIsolatedNodes = true,
                ValidateIntegrity = true
            };
    }
}
