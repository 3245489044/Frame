
using Share.TreeTraceFramework.Abstractions;
using Share.TreeTraceFramework.Core;

namespace Share.TreeTraceFramework.Serialization
{
    public class TraceRebuilder
    {
        public ITraceTree<TraceNode> RebuildFromConfigs(string traceId, IEnumerable<AddNodeConfig> nodeConfigs)
        {
            var tree = new TraceTree(traceId);
            var nodeMap = new Dictionary<string, TraceNode>();

            foreach (var config in nodeConfigs)
            {
                if (config == null) continue;

                TraceNode node;

                if (!string.IsNullOrEmpty(config.ParentNodeId) && nodeMap.TryGetValue(config.ParentNodeId, out var parent))
                {
                    node = parent.AddChild(config.Name);
                }
                else if (tree.Root == null)
                {
                    node = tree.CreateRoot(config.Name);
                }
                else
                {
                    node = tree.Root.AddChild(config.Name);
                }

                node.ApplyConfig(config);

                nodeMap[node.NodeId] = node;
            }

            return tree;
        }

        public ITraceTree<TraceNode> RebuildFromFlatList(string traceId, IEnumerable<FlatNodeInfo> nodes)
        {
            var tree = new TraceTree(traceId);
            var nodeMap = new Dictionary<string, TraceNode>();
            var nodeList = nodes?.Where(n => n != null).ToList();

            if (nodeList == null || nodeList.Count == 0)
                return tree;

            foreach (var info in nodeList)
            {
                if (string.IsNullOrEmpty(info.Name)) continue;

                TraceNode node;

                if (!string.IsNullOrEmpty(info.ParentNodeId) && nodeMap.TryGetValue(info.ParentNodeId, out var parent))
                {
                    node = parent.AddChild(info.Name);
                }
                else if (tree.Root == null)
                {
                    node = tree.CreateRoot(info.Name);
                }
                else
                {
                    node = tree.Root.AddChild(info.Name);
                }

                foreach (var input in info.Inputs) node.AddInput(input);
                foreach (var output in info.Outputs) node.AddOutput(output);
                foreach (var label in info.Labels) node.AddLabel(label);
                foreach (var record in info.Records) node.AddRecord(record);

                nodeMap[info.NodeId] = node;
            }

            foreach (var info in nodeList)
            {
                if (string.IsNullOrEmpty(info.ParentNodeId)) continue;

                if (nodeMap.TryGetValue(info.NodeId, out var node) &&
                    nodeMap.TryGetValue(info.ParentNodeId, out var correctParent))
                {
                    if (node.Parent != correctParent)
                    {
                        node.Detach();
                        var recreatedNode = correctParent.AddChild(info.Name);
                        foreach (var input in info.Inputs) recreatedNode.AddInput(input);
                        foreach (var output in info.Outputs) recreatedNode.AddOutput(output);
                        foreach (var label in info.Labels) recreatedNode.AddLabel(label);
                        foreach (var record in info.Records) recreatedNode.AddRecord(record);
                        nodeMap[info.NodeId] = recreatedNode;
                    }
                }
            }

            return tree;
        }
    }

    public class FlatNodeInfo
    {
        public string NodeId { get; set; } = string.Empty;

        public string? ParentNodeId { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<string> Labels { get; set; } = new();

        public List<object?> Inputs { get; set; } = new();

        public List<object?> Outputs { get; set; } = new();

        public List<string> Records { get; set; } = new();
    }
}
