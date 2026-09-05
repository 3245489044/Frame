using System;
using System.Collections.Generic;

namespace UniversalWorkflowFramework.BranchManagement.Models
{
    public class BranchGraph
    {
        public string BranchId { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public List<BranchNode> Nodes { get; set; }

        public List<BranchEdge> Edges { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public BranchGraph()
        {
            BranchId = string.Empty;
            Name = string.Empty;
            Description = string.Empty;
            Nodes = new List<BranchNode>();
            Edges = new List<BranchEdge>();
            CreatedAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public class BranchNode
    {
        public string NodeId { get; set; }

        public BranchNodeType Type { get; set; }

        public string Name { get; set; }

        public Dictionary<string, object> Data { get; set; }

        public BranchNode()
        {
            NodeId = string.Empty;
            Name = string.Empty;
            Data = new Dictionary<string, object>();
        }
    }

    public class BranchEdge
    {
        public string EdgeId { get; set; }

        public string SourceNodeId { get; set; }

        public string TargetNodeId { get; set; }

        public BranchEdgeType Type { get; set; }

        public Dictionary<string, object> Data { get; set; }

        public BranchEdge()
        {
            EdgeId = string.Empty;
            SourceNodeId = string.Empty;
            TargetNodeId = string.Empty;
            Data = new Dictionary<string, object>();
        }
    }

    public enum BranchNodeType
    {
        Start,

        Action,

        Decision,

        End,

        Subflow
    }

    public enum BranchEdgeType
    {
        Default,

        Normal,

        Conditional,

        Error
    }
}