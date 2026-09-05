#region USINGS
using Newtonsoft.Json;
using System;
using GraphFramework.Models;
using System.Collections.Generic;
using KeyPair = Common.GraphMindStudio.GlobalValue.DictionaryProcessor.KeyPair;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Models
{

    public class ChainConfigDto
    {
        public string ChainId { get; set; }
        public string WorkflowId { get; set; }
        public Chain<string, NodeConfig> Chain { get; set; }
        public string Pattern { get; set; }
        public string GeneratorType { get; set; }
        [JsonIgnore]
        public List<NodeDetailDto> NodeDetails { get; set; }
        public List<ConnectionDto> Connections { get; set; }
    }

    public class NodeDetailDto
    {
        public NodeConfig Node { get; set; }
        public bool NodeFuncExists { get; set; }
        public string NodeConfigDirectString { get; set; }
        public string NodeConfigSwitchReturn { get; set; }
        public bool? NodeConfigShow { get; set; }
        public string ChainPattern { get; set; }
        public string GeneratorType { get; set; }
    }

    public class ConnectionDto
    {
        public string FromNode { get; set; }
        public string ToNode { get; set; }
        public string FromWorkflow { get; set; }
        public string ToWorkflow { get; set; }
        public DateTime? ConnectionTime { get; set; }
        public string ConnectionType { get; set; } = "WorkflowConnection";
    }

    public class UniversalNodeDto
    {
        public List<string> Label { get; set; }
        public string Symbol { get; set; }
        public List<GetInputStrConfig> GetConfig { get; set; }
        public ExecuteConfig ExeConfig { get; set; } = new();
        public AddNodeConfigDto AddConfig { get; set; } = new();
        public ConnectNodeConfigDto ConnectConfig { get; set; } = new();
    }

    public class GetInputStrConfig
    {
        public string Getstring { get; set; }
        public List<string> Label { get; set; }
        public string Symbol { get; set; }
        public List<string> QuerySteps { get; set; }
    }

    public class AddNodeConfigDto
    {
        public string AddKey { get; set; }
        public List<string> Label { get; set; }
        public string Symbol { get; set; }
        public bool AutoCreateRoot { get; set; } = true;
        public bool AddAICallCellInvoke { get; set; } = true;
        public List<string> BackEdge { get; set; } = new();
        public List<string> ProcessedBackEdge { get; set; } = new();
        public IEnumerable<string> InputDatas { get; set; }
        public IEnumerable<string> OutputResult { get; set; }
        public List<string> SearchSet { get; set; }
        public string AddParameters { get; set; }
        public List<string> Record { get; set; } = new();
    }

    public class UniversalNode
    {
        public List<string> Label { get; set; }
        public string Symbol { get; set; }
        public List<GetInputStrConfig> GetConfig { get; set; }
        public ExecuteConfig ExeConfig { get; set; } = new();
        public AddNodeConfigDto AddConfig { get; set; } = new();
        public ConnectNodeConfigDto ConnectConfig { get; set; } = new();
    }

    public class ConnectNodeConfigDto
    {
        public bool HandleNextNodeRun { get; set; }
        public string NextNodeKey { get; set; }
        public string NodeKey { get; set; }
        public string AfterNodeCallkey { get; set; }
        public string BeforeNodeCallkey { get; set; }
        public bool UpdateChain { get; set; }
        public Dictionary<string, KeyPair> ConnectChainInfoKey { get; set; }
        public List<object> ConnectChainInfoConfig { get; set; }
        public List<object> MapKeyConfig { get; set; }
        public string? ConnectChainInfoConfigStr { get; set; }
        public string AfterSymbol { get; set; }
        public string BeforeSymbol { get; set; }
        public string SymbolMatchMode { get; set; } = "First";
        public bool IsInsertOperation => !string.IsNullOrEmpty(ConnectChainInfoConfigStr) || !string.IsNullOrEmpty(AfterSymbol) || !string.IsNullOrEmpty(BeforeSymbol);
    }
}
