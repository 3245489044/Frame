#region USINGS
using System;
using GraphFramework.Models;
using System.Collections.Generic;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Models
{

    public class ConnectionPositionInfo<TWorkflowId>
    {
        public int ListCount { get; set; }
        public Chain<string, NodeConfig> ChainInfo { get; set; }
        public string ChainConfigValue { get; set; }
        public string KeyMapping { get; set; }
        public string InsertAfterNodeCallkey { get; set; }
        public string InsertBeforeNodeCallkey { get; set; }
        public string InsertAfterSymbol { get; set; }
        public string InsertBeforeSymbol { get; set; }
        public string SymbolMatchMode { get; set; } = "First";
        public bool IsInsertOperation => InsertAfterNodeCallkey != null || InsertBeforeNodeCallkey != null || InsertAfterSymbol != null || InsertBeforeSymbol != null;
        public string TargetNodeKey => InsertAfterNodeCallkey ?? InsertBeforeNodeCallkey;
        public TWorkflowId WorkflowId { get; set; }
    }

    public class WorkflowConnection<TWorkflowId, TNodeId>
    {
        public TWorkflowId FromWorkflow { get; set; }
        public TWorkflowId ToWorkflow { get; set; }
        public TNodeId FromNode { get; set; }
        public TNodeId ToNode { get; set; }
        public DateTime ConnectionTime { get; set; }
    }

    public class ChainConfig
    {
        public List<ConnectionPositionInfo<string>> Chains { get; set; } = new();
        public string WorkflowId { get; set; }
    }
}
