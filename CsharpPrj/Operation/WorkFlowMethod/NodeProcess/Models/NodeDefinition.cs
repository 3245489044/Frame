#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using NodeConfig = WorkFlowMethod.Skeletons.NodeProcess.Models.NodeConfig;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Models
{

    public class EdgeDefinition
    {
        public string EdgeId { get; set; }
        public string FromNode { get; set; }
        public string ToNode { get; set; }
        public string SourceNodeKey { get; set; }
        public string TargetNodeKey { get; set; }
        public string Label { get; set; }
        public bool IsDirected { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new();

        public EdgeDefinition() { }
        public EdgeDefinition(string edgeId, string sourceNodeKey, string targetNodeKey, bool isDirected = false)
        {
            EdgeId = edgeId;
            SourceNodeKey = sourceNodeKey;
            TargetNodeKey = targetNodeKey;
            IsDirected = isDirected;
        }
    }

    public class NodeDefinition
    {
        public string Id { get; set; }
        public string NodeKey { get; set; }
        public string Symbol { get; set; }
        public string Label { get; set; }
        public List<string> Labels { get; set; } = new();
        public string Type { get; set; }
        public NodeConfig OriginalConfig { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new();

        public NodeDefinition() { }
        public NodeDefinition(string id, string symbol, string[] labels, NodeConfig originalConfig = null)
        {
            Id = id;
            NodeKey = id;
            Symbol = symbol;
            Labels = labels?.ToList() ?? new List<string>();
            Label = labels?.FirstOrDefault();
            OriginalConfig = originalConfig;
        }
    }

    public interface INodeConfigProvider
    {
        string CallKey { get; }
    }

}
