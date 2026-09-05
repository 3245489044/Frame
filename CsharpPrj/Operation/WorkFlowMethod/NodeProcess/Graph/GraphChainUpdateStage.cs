#region USINGS
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GraphFramework.Models;
using GraphFoundation.Core;
using SkeletonFlow;
using WorkflowExecution.Pipeline;
using GraphFramework.Directives;
using GraphFramework.Snapshots;
using WorkFlowMethod.Diagnostics;
using WorkFlowMethod.Execution.Session;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Graph
{
    public class GraphChainUpdateStage : SkeletonBase<PipelineContext, PipelineContext>
    {
        private readonly DefaultDirectiveApplier<string, NodeConfig, object> _updateService;

        public GraphChainUpdateStage(DefaultDirectiveApplier<string, NodeConfig, object> updateService = null)
            : base("GraphChainUpdateStage", "检查并更新工作流链，使用 GraphSnapshot<string, NodeConfig, object> 替代 ChainInfo")
        {
            _updateService = updateService ?? new DefaultDirectiveApplier<string, NodeConfig, object>();
        }

        protected override PipelineContext ExecuteCore(PipelineContext input, SkeletonContext context)
        {
            throw new NotSupportedException(
                $"GraphChainUpdateStage 不支持同步执行。" +
                $"请使用异步路径 ExecuteCoreAsync。");
        }

        protected override async Task<PipelineContext> ExecuteCoreAsync(PipelineContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var session = input.GetSession<ExecutionSession>();
            var snapshot = input.GetGraphSnapshot<GraphSnapshot<string, NodeConfig, object>>();
            if (snapshot == null)
            {
                WorkFlowMethodLog.Warn("GraphChainUpdate", "图快照为空，跳过链更新");
                return input;
            }

            try
            {
                WorkFlowMethodLog.Info("GraphChainUpdate",
                    $"开始检查工作流链更新: Graph={snapshot.GraphId}, Version={snapshot.Version}");

                var needsUpdate = await CheckIfUpdateNeededAsync(input, snapshot);

                if (needsUpdate)
                {
                    var directives = session?.Data?.Get<List<GraphUpdateDirective<string, NodeConfig, object>>>("PendingDirectives");

                    if (directives != null && directives.Count > 0)
                    {
                        WorkFlowMethodLog.Info("GraphChainUpdate",
                            $"发现 {directives.Count} 个待处理更新指令");

                        var updatedSnapshot = await UpdateWorkflowChainAsync(input, snapshot, directives);

                        if (updatedSnapshot != null)
                        {
                            input.SetGraphSnapshot(updatedSnapshot);
                            WorkFlowMethodLog.Info("GraphChainUpdate",
                                $"工作流链更新完成: NewVersion={updatedSnapshot.Version}");
                        }
                    }
                    else
                    {
                        WorkFlowMethodLog.Debug("GraphChainUpdate", "标记需要更新，但无待处理指令");
                    }
                }
                else
                {
                    WorkFlowMethodLog.Debug("GraphChainUpdate", "无需更新工作流链");
                }
            }
            catch (Exception ex)
            {
                WorkFlowMethodLog.Error("GraphChainUpdate",
                    $"工作流链更新失败: {ex.Message}", ex);
                throw;
            }

            return input;
        }

        protected virtual Task<bool> CheckIfUpdateNeededAsync(
            PipelineContext context,
            GraphSnapshot<string, NodeConfig, object> snapshot)
        {
            var updateFlag = context.GetSession<ExecutionSession>()?.Data?.Get<bool>("NeedsGraphUpdate");
            return Task.FromResult(updateFlag ?? false);
        }

        protected virtual Task<GraphSnapshot<string, NodeConfig, object>> UpdateWorkflowChainAsync(
            PipelineContext context,
            GraphSnapshot<string, NodeConfig, object> currentSnapshot,
            List<GraphUpdateDirective<string, NodeConfig, object>> directives)
        {
            var graph = new CoreGraph<string, NodeConfig, object>();
            currentSnapshot.ToGraph(graph);

            foreach (var directive in directives)
            {
                _updateService.Apply(graph, directive);
            }

            var updatedSnapshot = GraphSnapshot<string, NodeConfig, object>.CreateFrom(
                graph,
                currentSnapshot.GraphId,
                currentSnapshot.Version + 1);

            return Task.FromResult(updatedSnapshot);
        }
    }
}

