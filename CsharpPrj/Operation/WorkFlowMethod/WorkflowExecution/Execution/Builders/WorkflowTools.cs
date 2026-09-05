#region USINGS
using System;
using GraphFramework.Models;
using System.Collections.Generic;
using System.Linq;
using NewtonsoftJson = Newtonsoft.Json;
using WorkFlowMethod.Infrastructure;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WorkFlowMethod.Orchestration.IdManagement;
using WorkFlowMethod.Orchestration.Config;
#endregion

namespace WorkFlowMethod.Orchestration.Content
{
    public static class WorkflowTools
    {
        public static string GenerateCallkey(NodeConfig node)
        {
            return node?.basicConfig?.Callkey ?? IdServices.Generator.GenerateId();
        }

        public static string SerializeChain(Chain<string, NodeConfig> chain)
        {
            return NewtonsoftJson.JsonConvert.SerializeObject(chain, ServiceDefaults.SerializerSettings);
        }

        public static Chain<string, NodeConfig> DeserializeChain(string json)
        {
            return NewtonsoftJson.JsonConvert.DeserializeObject<ChainInfo<string, NodeConfig>>(json, ServiceDefaults.SerializerSettings);
        }

        public static NodeConfig CloneNode(NodeConfig source)
        {
            if (source == null) return null;
            var json = NewtonsoftJson.JsonConvert.SerializeObject(source, ServiceDefaults.SerializerSettings);
            return NewtonsoftJson.JsonConvert.DeserializeObject<NodeConfig>(json, ServiceDefaults.SerializerSettings);
        }

        public static List<string> GetAllCallkeys(Chain<string, NodeConfig> chain)
        {
            return chain?.AllNodes?
                .Select(n => n.basicConfig?.Callkey)
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList() ?? new List<string>();
        }

        public static bool IsChainValid(Chain<string, NodeConfig> chain)
        {
            try
            {
                ModularValidator.ConnectionValidator.ValidateChainConnectivity(chain);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
