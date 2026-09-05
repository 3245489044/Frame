#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WorkFlowMethod.Skeletons.NodeProcess;
using WorkFlowMethod.Skeletons.NodeProcess.Graph;
using GraphFoundation.Abstractions;
using SkeletonFlow;
using WorkFlowMethod.Execution.Session;
using WorkFlowMethod.GraphFramework.Pipeline;
using WorkFlowMethod.Skeletons.NodeProcess.Lifecycle;
#endregion

using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;


namespace WorkFlowMethod.Skeletons
{
    public static class PipelineBuilder
    {

        public static ISkeleton<WfPipelineContext, WfPipelineContext> BuildCustomPipeline(IGraph<string, NodeConfig, object> graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            ISkeleton<WfPipelineContext, WfPipelineContext> graphDef = new GraphDefinitionStage(graph);
            var pipeline = graphDef
                .Then(new NodeChainSkeleton())
                .Then(new NodeMetricsStage())
                .Then(new GraphChainUpdateStage());

            return pipeline;
        }

        public static ISkeleton<WfPipelineContext, WfPipelineContext> BuildBranchPipeline()
        {
            var shouldUpdate = new DelegateSkeleton<WfPipelineContext, bool>(
                "ShouldUpdate",
                syncWithCtx: (ctx, _) => ctx.GetSession<ExecutionSession>()?.Data?.Get<bool>("NeedsGraphUpdate") ?? false);

            var updateBranch = new GraphChainUpdateStage();
            var noopBranch = new DelegateSkeleton<WfPipelineContext, WfPipelineContext>(
                "NoOp",
                syncWithCtx: (ctx, _) => ctx);

            return shouldUpdate.Branch(updateBranch, noopBranch);
        }

        public static ISkeleton<WfPipelineContext, WfPipelineContext> BuildParallelPipeline()
        {
            var parallelPipeline = new NodeMetricsStage()
                .Then<WfPipelineContext, WfPipelineContext, WfPipelineContext>(
                    new DelegateSkeleton<WfPipelineContext, WfPipelineContext>(
                        "Identity",
                        syncWithCtx: (ctx, _) => ctx));

            return parallelPipeline;
        }
    }
}
