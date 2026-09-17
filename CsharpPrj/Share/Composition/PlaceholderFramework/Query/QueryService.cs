
using System.Text;
using Common.PlaceholderFramework.Core;
using Common.PlaceholderFramework.Storage;
using Modules.Pipeline;

namespace Common.PlaceholderFramework.Integrated.Query
{

    public class PlaceholderQuery
    {
        private readonly IMetadataStore _store;

        public PlaceholderQuery(IMetadataStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public BatchStatistics GetBatchStatistics(string batchId)
        {
            var all = _store.FindByBatch(batchId).ToList();
            return new BatchStatistics
            {
                BatchId = batchId,
                TotalCount = all.Count,
                KindDistribution = all.GroupBy(m => m.Kind)
                    .ToDictionary(g => g.Key, g => g.Count()),
                ModuleDistribution = all.GroupBy(m => m.SourceModule)
                    .ToDictionary(g => g.Key, g => g.Count()),
                TimeRange = all.Any()
                    ? ((DateTime? Min, DateTime? Max)?)(all.Min(m => m.Timestamp), all.Max(m => m.Timestamp))
                    : null
            };
        }

        public string FormatResults(IEnumerable<PlaceholderMetadata> results)
        {
            var sb = new System.Text.StringBuilder();
            var list = results.ToList();

            if (list.Count == 0)
            {
                sb.AppendLine("No results found.");
                return sb.ToString();
            }

            sb.AppendLine($"Found {list.Count} result(s):");
            sb.AppendLine(new string('-', 80));

            foreach (var item in list.OrderBy(m => m.Timestamp))
            {
                sb.AppendLine($"[{item.Timestamp:yyyy-MM-dd HH:mm:ss}] " +
                              $"[{item.Kind,-12}] " +
                              $"Key={item.Key,-20} " +
                              $"Value={item.ResolvedValue,-30} " +
                              $"Module={item.SourceModule}");
            }

            return sb.ToString();
        }
    }

    public class BatchStatistics
    {
        public string BatchId { get; set; }
        public int TotalCount { get; set; }
        public Dictionary<string, int> KindDistribution { get; set; }
        public Dictionary<string, int> ModuleDistribution { get; set; }
        public (DateTime? Min, DateTime? Max)? TimeRange { get; set; }

        public string ToFormattedText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Batch: {BatchId}");
            sb.AppendLine($"Total: {TotalCount}");

            if (TimeRange.HasValue)
            {
                sb.AppendLine($"Time Range: {TimeRange.Value.Min:yyyy-MM-dd HH:mm:ss} ~ {TimeRange.Value.Max:yyyy-MM-dd HH:mm:ss}");
            }
            else
            {
                sb.AppendLine("Time Range: (empty batch)");
            }

            sb.AppendLine();
            sb.AppendLine("Kind Distribution:");
            foreach (var kvp in KindDistribution ?? new Dictionary<string, int>())
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");

            sb.AppendLine();
            sb.AppendLine("Module Distribution:");
            foreach (var kvp in ModuleDistribution ?? new Dictionary<string, int>())
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");

            return sb.ToString();
        }
    }


    public class PlaceholderReverseResolver
    {
        private readonly IMetadataStore _store;

        public PlaceholderReverseResolver(IMetadataStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public CallChain RebuildCallChain(string batchId)
        {
            var allMetadata = _store.FindByBatch(batchId)
                .OrderBy(m => m.Timestamp)
                .ToList();

            var chain = new CallChain
            {
                BatchId = batchId,
                Steps = allMetadata.Select(m => new CallChainStep
                {
                    MetadataId = m.MetadataId,
                    Kind = m.Kind,
                    Key = m.Key,
                    ResolvedValue = m.ResolvedValue,
                    SourceModule = m.SourceModule,
                    HandlerName = m.HandlerName,
                    Timestamp = m.Timestamp
                }).ToList()
            };

            return chain;
        }

        public DataFlowGraph RebuildDataFlow(string batchId)
        {
            var allMetadata = _store.FindByBatch(batchId).ToList();
            var graph = new DataFlowGraph { BatchId = batchId };

            var nodeKeyMap = new Dictionary<string, string>();

            // Q2-2：逐元数据 Kind 分派收敛为 PipelineSwitch（分支表 = 该处唯一分派源）
            var dataFlowBranches = BuildDataFlowBranches();
            var branchContext = new DataFlowBranchContext(graph, allMetadata, nodeKeyMap);

            foreach (var metadata in allMetadata)
            {
                var compositeKey = $"{metadata.Key}|{metadata.Kind}";

                if (!nodeKeyMap.TryGetValue(compositeKey, out var nodeId))
                {
                    nodeId = metadata.MetadataId;
                    nodeKeyMap[compositeKey] = nodeId;

                    graph.Nodes[nodeId] = new DataFlowNode
                    {
                        Id = nodeId,
                        Key = metadata.Key,
                        Kind = metadata.Kind,
                        Value = metadata.ResolvedValue,
                        Module = metadata.SourceModule,
                        Handler = metadata.HandlerName,
                        Timestamp = metadata.Timestamp
                    };
                }

                // Q2-2：Kind 分派经 PipelineSwitch；无匹配 = 无操作（与迁移前「switch 无 default」同语义）
                branchContext.Current = metadata;
                branchContext.NodeId = nodeId;
                dataFlowBranches.Execute(branchContext);
            }

            return graph;
        }

        public DataSourceTrace TraceDataSource(string batchId, string metadataId)
        {
            var allMetadata = _store.FindByBatch(batchId)
                .OrderBy(m => m.Timestamp)
                .ToList();

            var target = allMetadata.FirstOrDefault(m => m.MetadataId == metadataId);
            if (target == null)
                return null;

            var trace = new DataSourceTrace
            {
                TargetMetadataId = metadataId,
                TargetKey = target.Key,
                TargetKind = target.Kind,
                TargetValue = target.ResolvedValue
            };

            // Q2-2：Kind 分派经 PipelineSwitch（与数据流重建处**同构但不合并**——取值语义不同）
            BuildTraceSourceBranches().Execute(new TraceSourceContext(allMetadata, target, trace));

            return trace;
        }

        public IEnumerable<DataSink> TraceDataSink(string batchId, string metadataId)
        {
            var allMetadata = _store.FindByBatch(batchId).ToList();
            var target = allMetadata.FirstOrDefault(m => m.MetadataId == metadataId);
            if (target == null)
                yield break;

            foreach (var metadata in allMetadata)
            {
                if (metadata.MetadataId == metadataId) continue;

                var isSink = false;
                var relation = string.Empty;

                if (metadata.Kind == "Prev" && metadata.Timestamp >= target.Timestamp)
                {
                    isSink = true;
                    relation = "prev reference";
                }

                if (metadata.Kind == "NodeReference"
                    && string.Equals(metadata.NodeId, target.NodeId, StringComparison.OrdinalIgnoreCase))
                {
                    isSink = true;
                    relation = "node reference";
                }

                if (isSink)
                {
                    yield return new DataSink
                    {
                        SinkMetadataId = metadata.MetadataId,
                        SinkKey = metadata.Key,
                        SinkKind = metadata.Kind,
                        SinkValue = metadata.ResolvedValue,
                        Relation = relation,
                        Timestamp = metadata.Timestamp
                    };
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Q2-2：两处 Kind 分派（数据流重建 / 数据源追溯）各自独立收敛为 PipelineSwitch。
        // 纪律：① 两处语义不同（图增补 vs 来源描述），**禁合并为一条管线**，只统一分派机制；
        //       ② 缺省分支 = 无操作 —— 保持迁移前「switch 无 default（未知 Kind 无副作用）」语义，
        //          禁借 PipelineSwitch 缺省「无匹配即抛」改变既有行为。
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>分支包装：动作体 → 同上下文管线（分支体与迁移前逐字一致；机制由框架 <c>AsBranch</c> 单源提供）。</summary>
        private static IPipeline<TContext, TContext> Branch<TContext>(Action<TContext> body)
            => body.AsBranch("KindBranch");

        /// <summary>数据流重建分支表（Kind → 图增补）。</summary>
        private static PipelineSwitch<DataFlowBranchContext, string> BuildDataFlowBranches()
            => new(
                context => context.Current.Kind,
                new Dictionary<string, IPipeline<DataFlowBranchContext, DataFlowBranchContext>>(StringComparer.Ordinal)
                {
                    ["Input"] = Branch<DataFlowBranchContext>(context =>
                    {
                        if (!context.Graph.Inputs.Contains(context.NodeId))
                            context.Graph.Inputs.Add(context.NodeId);
                    }),

                    ["Prev"] = Branch<DataFlowBranchContext>(context =>
                    {
                        var metadata = context.Current;
                        var prevNode = context.AllMetadata
                            .Where(m => m.Timestamp <= metadata.Timestamp
                                        && !(m.Key == metadata.Key && m.Kind == metadata.Kind))
                            .OrderByDescending(m => m.Timestamp)
                            .FirstOrDefault();
                        if (prevNode != null)
                        {
                            var prevCompositeKey = $"{prevNode.Key}|{prevNode.Kind}";
                            if (context.NodeKeyMap.TryGetValue(prevCompositeKey, out var prevNodeId))
                            {
                                context.Graph.Edges.Add(new DataFlowEdge
                                {
                                    From = prevNodeId,
                                    To = context.NodeId,
                                    Label = $"prev:{metadata.Key}"
                                });
                            }
                        }
                    }),

                    ["NodeReference"] = Branch<DataFlowBranchContext>(context =>
                    {
                        var metadata = context.Current;
                        if (!string.IsNullOrEmpty(metadata.NodeId))
                        {
                            var refNode = context.AllMetadata
                                .FirstOrDefault(m => m.NodeId == metadata.NodeId);
                            if (refNode != null)
                            {
                                var refCompositeKey = $"{refNode.Key}|{refNode.Kind}";
                                if (context.NodeKeyMap.TryGetValue(refCompositeKey, out var refNodeId))
                                {
                                    context.Graph.Edges.Add(new DataFlowEdge
                                    {
                                        From = refNodeId,
                                        To = context.NodeId,
                                        Label = $"ref:{metadata.NodeId}"
                                    });
                                }
                            }
                        }
                    }),
                },
                defaultBranch: Branch<DataFlowBranchContext>(_ => { }),
                name: "DataFlowKindBranches",
                description: "数据流重建：Kind 逐类型分派（Q2-2）");

        /// <summary>数据流分支上下文（一次 <c>RebuildDataFlow</c> 内复用，逐项只换 <see cref="Current"/> / <see cref="NodeId"/>）。</summary>
        private sealed class DataFlowBranchContext(
            DataFlowGraph graph,
            List<PlaceholderMetadata> allMetadata,
            Dictionary<string, string> nodeKeyMap)
        {
            internal DataFlowGraph Graph { get; } = graph;

            internal List<PlaceholderMetadata> AllMetadata { get; } = allMetadata;

            internal Dictionary<string, string> NodeKeyMap { get; } = nodeKeyMap;

            internal PlaceholderMetadata Current { get; set; } = null!;

            internal string NodeId { get; set; } = string.Empty;
        }

        /// <summary>数据源追溯分支表（Kind → 来源描述）。</summary>
        private static PipelineSwitch<TraceSourceContext, string> BuildTraceSourceBranches()
            => new(
                context => context.Target.Kind,
                new Dictionary<string, IPipeline<TraceSourceContext, TraceSourceContext>>(StringComparer.Ordinal)
                {
                    ["Input"] = Branch<TraceSourceContext>(context =>
                    {
                        context.Trace.SourceType = "ExternalInput";
                        context.Trace.SourceDescription = $"Input key: {context.Target.Key}";
                    }),

                    ["Prev"] = Branch<TraceSourceContext>(context =>
                    {
                        var target = context.Target;
                        var prevSource = context.AllMetadata
                            .Where(m => m.Timestamp <= target.Timestamp
                                        && m.MetadataId != target.MetadataId)
                            .OrderByDescending(m => m.Timestamp)
                            .FirstOrDefault();
                        if (prevSource != null)
                        {
                            context.Trace.SourceType = "PreviousStep";
                            context.Trace.SourceMetadataId = prevSource.MetadataId;
                            context.Trace.SourceDescription = $"From previous step: {prevSource.Key} = {prevSource.ResolvedValue}";
                        }
                    }),

                    ["NodeReference"] = Branch<TraceSourceContext>(context =>
                    {
                        var target = context.Target;
                        context.Trace.SourceType = "NodeReference";
                        context.Trace.SourceDescription = $"From node: {target.NodeId}";
                        var nodeSource = context.AllMetadata
                            .FirstOrDefault(m => m.NodeId == target.NodeId);
                        if (nodeSource != null)
                        {
                            context.Trace.SourceMetadataId = nodeSource.MetadataId;
                        }
                    }),

                    ["NodeIndex"] = Branch<TraceSourceContext>(context =>
                    {
                        context.Trace.SourceType = "NodeIndex";
                        context.Trace.SourceDescription = $"From node index: {context.Target.NodeIndex}";
                    }),
                },
                defaultBranch: Branch<TraceSourceContext>(_ => { }),
                name: "TraceSourceKindBranches",
                description: "数据源追溯：Kind 逐类型分派（Q2-2）");

        /// <summary>数据源追溯分支上下文。</summary>
        private sealed class TraceSourceContext(
            List<PlaceholderMetadata> allMetadata,
            PlaceholderMetadata target,
            DataSourceTrace trace)
        {
            internal List<PlaceholderMetadata> AllMetadata { get; } = allMetadata;

            internal PlaceholderMetadata Target { get; } = target;

            internal DataSourceTrace Trace { get; } = trace;
        }
    }

    public class CallChain
    {
        public string BatchId { get; set; }
        public List<CallChainStep> Steps { get; set; } = new();

        public string ToFormattedText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Call Chain for Batch: {BatchId}");
            sb.AppendLine(new string('-', 80));

            for (int i = 0; i < Steps.Count; i++)
            {
                var step = Steps[i];
                sb.AppendLine($"  Step {i + 1}: [{step.Timestamp:HH:mm:ss}] " +
                              $"[{step.Kind}] {step.Key} = {step.ResolvedValue} " +
                              $"(Handler: {step.HandlerName}, Module: {step.SourceModule})");
            }

            return sb.ToString();
        }
    }

    public class CallChainStep
    {
        public string MetadataId { get; set; }
        public string Kind { get; set; }
        public string Key { get; set; }
        public string ResolvedValue { get; set; }
        public string SourceModule { get; set; }
        public string HandlerName { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class DataFlowGraph
    {
        public string BatchId { get; set; }
        public Dictionary<string, DataFlowNode> Nodes { get; set; } = new();
        public List<DataFlowEdge> Edges { get; set; } = new();
        public List<string> Inputs { get; set; } = new();

        public string ToFormattedText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Data Flow Graph for Batch: {BatchId}");
            sb.AppendLine(new string('-', 80));
            sb.AppendLine($"Nodes: {Nodes.Count}, Edges: {Edges.Count}, Inputs: {Inputs.Count}");
            sb.AppendLine();

            sb.AppendLine("Inputs:");
            foreach (var input in Inputs)
            {
                if (Nodes.TryGetValue(input, out var node))
                    sb.AppendLine($"  -> {node.Key} = {node.Value}");
            }

            sb.AppendLine();
            sb.AppendLine("Edges:");
            foreach (var edge in Edges)
            {
                var fromNode = Nodes.TryGetValue(edge.From, out var fn) ? fn.Key : edge.From;
                var toNode = Nodes.TryGetValue(edge.To, out var tn) ? tn.Key : edge.To;
                sb.AppendLine($"  {fromNode} --[{edge.Label}]--> {toNode}");
            }

            return sb.ToString();
        }
    }

    public class DataFlowNode
    {
        public string Id { get; set; }
        public string Key { get; set; }
        public string Kind { get; set; }
        public string Value { get; set; }
        public string Module { get; set; }
        public string Handler { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class DataFlowEdge
    {
        public string From { get; set; }
        public string To { get; set; }
        public string Label { get; set; }
    }

    public class DataSourceTrace
    {
        public string TargetMetadataId { get; set; }
        public string TargetKey { get; set; }
        public string TargetKind { get; set; }
        public string TargetValue { get; set; }
        public string SourceType { get; set; }
        public string SourceMetadataId { get; set; }
        public string SourceDescription { get; set; }
    }

    public class DataSink
    {
        public string SinkMetadataId { get; set; }
        public string SinkKey { get; set; }
        public string SinkKind { get; set; }
        public string SinkValue { get; set; }
        public string Relation { get; set; }
        public DateTime Timestamp { get; set; }
    }


    public class TreeNode
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? SourceFile { get; set; }
        public List<TreeNode> Children { get; set; } = new();
        public int Depth { get; set; }
        public bool HasIssues { get; set; }
        public List<string> Issues { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }


    public static class TreeAdapter
    {
        public static string ToText(TreeNode node, int maxDepth = int.MaxValue)
        {
            var sb = new StringBuilder();
            AppendNode(sb, node, 0, maxDepth);
            return sb.ToString();
        }

        public static string ToText(IEnumerable<TreeNode> roots, int maxDepth = int.MaxValue)
        {
            var sb = new StringBuilder();
            foreach (var root in roots)
                AppendNode(sb, root, 0, maxDepth);
            return sb.ToString();
        }

        public static TreeNode CreateRoot(string name, string type = "root", string fullPath = "$")
            => new() { Name = name, Type = type, FullPath = fullPath, Depth = 0 };

        public static TreeNode CreateChild(string name, string type, string fullPath, int depth, string? description = null)
            => new() { Name = name, Type = type, FullPath = fullPath, Depth = depth, Description = description };

        public static TreeNode AddChild(this TreeNode parent, TreeNode child)
        {
            child.Depth = parent.Depth + 1;
            parent.Children.Add(child);
            return parent;
        }

        public static TreeNode AddIssue(this TreeNode node, string issue)
        {
            node.Issues.Add(issue);
            node.HasIssues = true;
            return node;
        }

        private static void AppendNode(StringBuilder sb, TreeNode node, int indent, int maxDepth)
        {
            if (indent > maxDepth) return;
            var prefix = new string(' ', indent * 2);
            var issues = node.HasIssues ? " ⚠️" : "";
            sb.AppendLine($"{prefix}{GetTypeIcon(node.Type)} {node.Name}{issues}");
            if (!string.IsNullOrEmpty(node.Description))
                sb.AppendLine($"{prefix}  └─ {node.Description}");
            foreach (var issue in node.Issues)
                sb.AppendLine($"{prefix}  ⚠ {issue}");
            foreach (var child in node.Children)
                AppendNode(sb, child, indent + 1, maxDepth);
        }

        private static string GetTypeIcon(string type) => type.ToLowerInvariant() switch
        {
            "template" => "📄",
            "style" => "🎨",
            "extends" => "🔗",
            "root" => "📁",
            "module" => "📦",
            "service" => "🔧",
            "data" => "📊",
            "config" => "⚙️",
            "log" => "📝",
            _ => "📄"
        };
    }


    public class QueryService
    {
        private readonly PlaceholderQuery _query;
        private readonly PlaceholderReverseResolver _resolver;

        public QueryService(IMetadataStore store)
        {
            _query = new PlaceholderQuery(store);
            _resolver = new PlaceholderReverseResolver(store);
        }

        public BatchStatistics GetBatchStatistics(string batchId)
            => _query.GetBatchStatistics(batchId);

        public CallChain RebuildCallChain(string batchId)
            => _resolver.RebuildCallChain(batchId);

        public DataFlowGraph RebuildDataFlow(string batchId)
            => _resolver.RebuildDataFlow(batchId);

        public DataSourceTrace TraceDataSource(string batchId, string metadataId)
            => _resolver.TraceDataSource(batchId, metadataId);

        public IEnumerable<DataSink> TraceDataSink(string batchId, string metadataId)
            => _resolver.TraceDataSink(batchId, metadataId);
    }
}
