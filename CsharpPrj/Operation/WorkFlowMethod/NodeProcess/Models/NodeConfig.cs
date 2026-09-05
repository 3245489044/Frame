#region USINGS
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using KeyPair = Common.GraphMindStudio.GlobalValue.DictionaryProcessor.KeyPair;
using INodeConfigProvider = WorkFlowMethod.Skeletons.NodeProcess.Models.INodeConfigProvider;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Models
{

    public class NodeConfig : INodeConfigProvider
    {
        public string Id { get; set; }
        public string NextNodeId { get; set; }
        public string Symbol { get; set; }
        public string Label { get; set; }
        public GetStrConfig getStrConfig { get; set; } = new();
        public FuncPipelineConfig funcPipelineConfig { get; set; } = new();
        public AddNodeConfig addNodeConfig { get; set; } = new();
        public BasicConfig basicConfig { get; set; } = new();
        public NextNodeConfig nextNodeConfig { get; set; } = new();

        string INodeConfigProvider.CallKey => basicConfig?.Callkey;

        #region 子配置类

        public class GetStrConfig
        {
            public string GetConfigLabel { get; set; }
            public string GetConfigSymbol { get; set; }
            public string GetConfigGetstring { get; set; }
            public string GetConfigQuerySteps { get; set; }
        }

        public class FuncPipelineConfig
        {
            public string ParameterKeys { get; set; }
            public string ResultKeys { get; set; }
            public string ProcessSymbol { get; set; }
            public string FuncName { get; set; }
            public string ParameterType { get; set; }
            public string Params { get; set; }
        }

        public class AddNodeConfig
        {
            public string AddConfigAddKey { get; set; }
            public bool AddAICallCellInvoke { get; set; } = true;
            public bool AddAutoCreateRoot { get; set; } = true;
            public string AddParameters { get; set; }
        }

        public class NextNodeConfig
        {
            public bool HandleNextNodeRun { get; set; } = true;
            public string NextNodeKey { get; set; }
            public string MapKeyConfig { get; set; }
            public string ConnectChainInfoKey { get; set; }
            public string ConnectChainInfoConfig { get; set; }
            public bool UpdateChain { get; set; } = false;
            public string BeforeNodeCallkey { get; set; }
            public string AfterNodeCallkey { get; set; }
            public string AfterSymbol { get; set; }
            public string BeforeSymbol { get; set; }
            public string SymbolMatchMode { get; set; } = "First";
        }

        public class BasicConfig
        {
            public string Callkey { get; set; }
            public string DirectString { get; set; } = "DirectINPUT";
            public bool Show { get; set; } = true;
            public string SwitchReturn { get; set; }
        }

        #endregion
    }
}
