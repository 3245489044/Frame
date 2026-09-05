#region USINGS
using System;
using System.Collections.Generic;
using Modules.Transformers;
using Modules.GraphProcessing.Normalization;
using Modules.GraphProcessing.Orchestration;
using Modules.GraphProcessing.Reference;
using GraphFoundation.Topology.Transformers;
using WorkflowExecution.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.GraphFramework.Deduplication
{
    public class DeduplicationConfig : INormalizationConfig
    {
        public const int DefaultMaxDuplicates = 100;
        public const string DefaultRenamePattern = "{0}_chain{1}_dup{2}";
        public const string DefaultCrossChainReferencePrefix = "cross_";

        private string _renamePattern = DefaultRenamePattern;
        public string RenamePattern
        {
            get => _renamePattern;
            set
            {
                if (!value.Contains("{0}") || !value.Contains("{1}") || !value.Contains("{2}"))
                    throw new ArgumentException("RenamePattern must contain {0}, {1}, {2} placeholders.");
                _renamePattern = value;
            }
        }
        public int MaxDuplicates { get; set; } = DefaultMaxDuplicates;
        public bool UpdateNodeReferences { get; set; } = true;
        public bool PreserveFirstOccurrence { get; set; } = true;
        public bool HandleCrossChainReferences { get; set; } = true;
        public string CrossChainReferencePrefix { get; set; } = DefaultCrossChainReferencePrefix;
        public bool EnforceUniqueSymbols { get; set; } = true;
        public CrossChainReferenceStrategy CrossChainStrategy { get; set; } = CrossChainReferenceStrategy.Preserve;
        public int? CrossChainTargetChainIndex { get; set; }
        public bool EnableNormalization { get; set; } = true;
        public bool MergeDuplicates { get; set; } = true;
        public bool EnableParallelProcessing { get; set; } = false;
        public int ParallelDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

        public bool RemoveIsolatedNodes { get; set; } = true;
        public bool NormalizeIdentifiers { get; set; } = true;
        public bool MergeDuplicateEdges { get; set; } = true;
        public bool ValidateIntegrity { get; set; } = true;
        public bool VerboseLogging { get; set; } = false;
        public IMatchStrategy<Connection, object>? DedupeMatchStrategy { get; set; }
        public IReduceStrategy<Connection>? DedupeReduceStrategy { get; set; }

        public List<FieldMappingEntry> FieldMappings { get; set; } = new();
        public bool EnableFieldMapping { get; set; } = true;

        public static readonly DeduplicationConfig DEFAULT = new();

        public static readonly DeduplicationConfig STRICT = new()
        {
            MaxDuplicates = 50,
            EnforceUniqueSymbols = true,
            MergeDuplicates = false,
            PreserveFirstOccurrence = true,
            ValidateIntegrity = true
        };

        public static readonly DeduplicationConfig RELAXED = new()
        {
            MaxDuplicates = 200,
            EnforceUniqueSymbols = false,
            MergeDuplicates = true,
            PreserveFirstOccurrence = true,
            ValidateIntegrity = false
        };

        public DeduplicationConfig()
        {
            FieldMappings.Add(new FieldMappingEntry
            {
                SourcePropertyPath = "AddNodeConfig.AddConfigAddKey",
                TargetPropertyPath = "Symbol",
                Mode = FieldMappingMode.ByValueAsNodeIdentifier
            });
        }
    }
}
