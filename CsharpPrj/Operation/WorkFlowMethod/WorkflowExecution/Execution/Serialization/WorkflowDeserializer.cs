#region USINGS
using System;
using GraphFramework.Models;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.Orchestration.Content
{
    public interface IWorkflowDeserializer
    {
        Chain<string, NodeConfig> Deserialize(string json);
        Chain<string, TNode> Deserialize<TNode>(string json) where TNode : NodeConfig;
    }

    // 反序列化统一走 UniversalMiddlewareEntry（内部委托 Plate.JsonMethod.JsonSerializationService，System.Text.Json 单轨）。
    // 原 NewtonsoftJson.JsonException 捕获为死分支（实际抛的是 System.Text.Json.JsonException），已移除；
    // 解析失败时由 JsonSerializationService 抛出的带路径诊断的 JsonException 直接上抛（行为与迁移前一致）。
    public class WorkflowDeserializer : IWorkflowDeserializer
    {
        public Chain<string, NodeConfig> Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            return UniversalMiddleware.UniversalMiddlewareEntry.FromJson<ChainInfo<string, NodeConfig>>(json);
        }

        public Chain<string, TNode> Deserialize<TNode>(string json) where TNode : NodeConfig
        {
            if (string.IsNullOrEmpty(json)) return null;
            return UniversalMiddleware.UniversalMiddlewareEntry.FromJson<Chain<string, TNode>>(json);
        }
    }
}
