#region USINGS
using System;
using System.Linq;
using System.Threading.Tasks;
using GraphFoundation;
using GraphFoundation.Abstractions;
using GraphFoundation.Core;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using SkeletonFlow;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.Diagnostics;
using WorkFlowMethod.Execution.Engine;
using GraphFramework.Snapshots;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Graph
{
    public class GraphDefinitionStage : SkeletonBase<PipelineContext, PipelineContext>
    {
        private readonly IGraph<string, NodeConfig, object> _graph;
        private readonly SessionFactory _sessionFactory;

        public GraphDefinitionStage(IGraph<string, NodeConfig, object> graph, SessionFactory sessionFactory = null)
            : base("GraphDefinitionStage", "从 IGraph 创建 GraphSnapshot<string, NodeConfig, object> 和 ExecutionSession")
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _sessionFactory = sessionFactory ?? new SessionFactory();
        }

        protected override PipelineContext ExecuteCore(PipelineContext input, SkeletonContext context)
        {
            throw new NotSupportedException(
                $"GraphDefinitionStage 不支持同步执行。" +
                $"请使用异步路径 ExecuteCoreAsync。");
        }

        protected override async Task<PipelineContext> ExecuteCoreAsync(PipelineContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var snapshot = GraphSnapshot<string, NodeConfig, object>.CreateFrom(_graph, "GraphDefinition");
            input.SetGraphSnapshot(snapshot);

            var allKeys = _graph.GetAllNodeKeys();
            if (allKeys.Count == 0)
            {
                WorkFlowMethodLog.Warn("GraphDefinition", "图快照中没有节点");
                throw new InvalidOperationException("图快照为空，无法执行工作流");
            }
            else
            {
                WorkFlowMethodLog.Info("GraphDefinition", $"图快照已创建: {snapshot.SnapshotId}, 节点数: {allKeys.Count}");
            }

            if (input.Session == null)
            {
                var newSession = _sessionFactory.CreateSession(snapshot);
                input.SetSession(newSession);
                WorkFlowMethodLog.Info("GraphDefinition", $"执行会话已创建: {newSession.SessionId}");
            }

            var entryNodeKeys = snapshot.GetEntryNodeKeys();
            if (entryNodeKeys.Count == 0)
            {
                WorkFlowMethodLog.Error("GraphDefinition", "图中没有入口节点");
                throw new InvalidOperationException("图中没有入口节点");
            }

            if (string.IsNullOrEmpty(input.CurrentNodeKey))
            {
                input.CurrentNodeKey = entryNodeKeys[0];
                WorkFlowMethodLog.Info("GraphDefinition", $"入口节点已设置: {entryNodeKeys[0]}");
            }
            else
            {
                WorkFlowMethodLog.Debug("GraphDefinition",
                    $"入口节点已在之前阶段设置: {input.CurrentNodeKey}");
            }

            return input;
        }
    }
}

