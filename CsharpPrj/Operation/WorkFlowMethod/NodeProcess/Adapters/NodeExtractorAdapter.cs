#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkeletonFlow;
using WorkflowExecution.Pipeline;
using GraphFramework.Snapshots;
using WorkFlowMethod.Diagnostics;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Adapters
{
    public class NodeExtractorAdapter : SkeletonBase<PipelineContext, PipelineContext>
    {
        public NodeExtractorAdapter()
            : base("NodeExtractorAdapter", "从 PipelineContext 中提取节点信息")
        {
        }

        protected override PipelineContext ExecuteCore(PipelineContext input, SkeletonContext context)
        {
            throw new NotSupportedException(
                $"NodeExtractorAdapter 不支持同步执行。" +
                $"请使用异步路径 ExecuteCoreAsync。");
        }

        protected override async Task<PipelineContext> ExecuteCoreAsync(PipelineContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var snapshot = input.GetGraphSnapshot<GraphSnapshot<string, NodeConfig, object>>();
            if (snapshot == null)
            {
                WorkFlowMethodLog.Warn("NodeExtractor", "图快照为空，跳过节点提取");
                return input;
            }

            var nodeKeys = snapshot.NodeKeys.ToList();
            var nodeInfos = new List<NodeInfo>();

            foreach (var nodeKey in nodeKeys)
            {
                var nodeData = snapshot.GetNodeData(nodeKey);
                var previousNodes = snapshot.GetPreviousNodeKeys(nodeKey);
                var nextNodes = snapshot.GetNextNodeKeys(nodeKey);

                nodeInfos.Add(new NodeInfo
                {
                    NodeKey = nodeKey,
                    Symbol = nodeData?.ToString() ?? nodeKey,
                    PreviousNodes = previousNodes.ToList(),
                    NextNodes = nextNodes.ToList(),
                    IsEntry = previousNodes.Count == 0,
                    IsExit = nextNodes.Count == 0
                });
            }

            input.SetData("ExtractedNodes", nodeInfos);
            input.SetData("ExtractedNodeCount", nodeInfos.Count);

            WorkFlowMethodLog.Info("NodeExtractor",
                $"节点提取完成: 总数={nodeInfos.Count}, " +
                $"入口={nodeInfos.Count(n => n.IsEntry)}, " +
                $"出口={nodeInfos.Count(n => n.IsExit)}");

            return input;
        }
    }

    public class NodeInfo
    {
        public string NodeKey { get; set; }

        public string Symbol { get; set; }

        public List<string> PreviousNodes { get; set; } = new();

        public List<string> NextNodes { get; set; } = new();

        public bool IsEntry { get; set; }

        public bool IsExit { get; set; }
    }
}

