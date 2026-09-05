
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Pipeline;
using SkeletonFlow;
using Share.StateManagement;
using WorkFlowMethod.Execution.Engine;
using WorkFlowMethod.Diagnostics;
using GraphFramework.Directives;
using GraphFramework.Execution;
using GraphFramework.Models;
using GraphFramework.Snapshots;
using WorkFlowMethod.Infrastructure.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Adapters;
using WorkFlowMethod.Skeletons.NodeProcess.Execution;
using WorkFlowMethod.Skeletons.NodeProcess.Graph;
using WorkFlowMethod.Skeletons.NodeProcess.Lifecycle;
using WorkFlowMethod.Skeletons.NodeProcess.Models;

using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;
using SMExecutionStatus = Share.StateManagement.ExecutionStatus;

namespace WorkFlowMethod.Skeletons.NodeProcess
{
    public class NodeProcessPipelineBuilder
    {
        private readonly StagePipeline<WfPipelineContext> _pipeline;

        public NodeProcessPipelineBuilder(string? name = null)
        {
            _pipeline = new StagePipeline<WfPipelineContext>(
                name ?? "NodeProcessPipeline",
                "NodeProcess 完整执行管线");
        }


        public NodeProcessPipelineBuilder AddGraphDefinition(
            GraphFoundation.Abstractions.IGraph<string, NodeConfig, object> graph,
            SessionFactory? sessionFactory = null)
        {
            var stage = new GraphDefinitionStage(graph, sessionFactory);
            _pipeline.Register(new SkeletonToStageAdapter<WfPipelineContext>(stage));
            return this;
        }

        public NodeProcessPipelineBuilder AddNodeChain(
            INodeChainService? chainService = null,
            IWorkflowRuntimeContextAccessor? contextAccessor = null)
        {
            var stage = new NodeChainSkeleton(chainService, contextAccessor);
            _pipeline.Register(new SkeletonToStageAdapter<WfPipelineContext>(stage));
            return this;
        }

        public NodeProcessPipelineBuilder AddNodeExtractor()
        {
            var adapter = new NodeExtractorAdapter();
            _pipeline.Register(new SkeletonToStageAdapter<WfPipelineContext>(adapter));
            return this;
        }

        public NodeProcessPipelineBuilder AddGraphChainUpdate(
            IContext? context = null,
            DefaultDirectiveApplier<string, NodeConfig, object>? updateService = null)
        {
            var stage = new GraphChainUpdateStage(updateService);
            _pipeline.Register(new SkeletonToStageAdapter<WfPipelineContext>(stage));
            return this;
        }

        public NodeProcessPipelineBuilder AddNodeExecution(
            IExecutionStateStore<SMExecutionStatus>? stateStore = null,
            Share.StateManagement.INodeExecutionCoordinator? coordinator = null,
            bool enableErrorHandling = true)
        {
            var executionStage = new NodeExecutionStage(stateStore, coordinator);

            if (enableErrorHandling)
            {
                var stagePipeline = PipelineUsage.CreateStage<WfPipelineContext>(
                    name: "NodeExecution",
                    action: (ctx, pipelineCtx) =>
                    {
                        var cts = CancellationTokenSource.CreateLinkedTokenSource(pipelineCtx.CancellationToken);
                        executionStage.ExecuteAsync(ctx, cts.Token).GetAwaiter().GetResult();
                    });

                var tryCatchPipeline = PipelineUsage.TryCatch<WfPipelineContext>(
                    mainPipeline: stagePipeline,
                    fallbackPipeline: null,
                    onError: ex => WorkFlowMethodLog.Log("NodeProcessPipelineBuilder_NodeExecution_error",
                        new { error = ex.Message }),
                    name: "NodeExecution.TryCatch");

                _pipeline.Register(new PipelineToStageAdapter(tryCatchPipeline));
            }
            else
            {
                _pipeline.Register(executionStage);
            }

            return this;
        }

        public NodeProcessPipelineBuilder AddNodeLoop(
            INodeProcessor<string, NodeConfig, object, WfPipelineContext>? nodeProcessor = null,
            int maxIterations = 10000)
        {
            var stepSkeleton = new NodeExecutionStepSkeleton(
                nodeProcessor != null ? new NodeChainSkeleton() : null);

            var stepPipeline = PipelineUsage.CreateStage<WfPipelineContext>(
                name: "NodeLoopStep",
                action: (ctx, pipelineCtx) =>
                {
                    var skeletonCtx = new SkeletonContext();
                    stepSkeleton.ExecuteAsync(ctx, skeletonCtx).GetAwaiter().GetResult();
                });

            var loopPipeline = PipelineUsage.Loop<WfPipelineContext>(
                predicate: (ctx) =>
                {
                    if (string.IsNullOrEmpty(ctx.CurrentNodeKey))
                        return false;

                    var executionStageResult = ctx.GetStageSuccess("NodeExecution");
                    if (!executionStageResult)
                    {
                        var snapshot = ctx.GetGraphSnapshot<GraphSnapshot<string, NodeConfig, object>>();
                        if (snapshot != null && !string.IsNullOrEmpty(ctx.CurrentNodeKey))
                        {
                            var nextKeys = snapshot.GetNextNodeKeys(ctx.CurrentNodeKey);
                            if (nextKeys == null || nextKeys.Count == 0)
                                return false;
                        }
                        else
                        {
                            return false;
                        }
                    }

                    var loopCount = ctx.GetData<int>("NodeLoop.IterationCount");
                    if (loopCount >= maxIterations)
                    {
                        WorkFlowMethodLog.Warn("NodeLoop",
                            $"循环次数超过最大限制 ({maxIterations})，强制终止");
                        return false;
                    }

                    return true;
                },
                body: stepPipeline);

            _pipeline.Register(new PipelineToStageAdapter(loopPipeline));
            return this;
        }

        public NodeProcessPipelineBuilder AddNodeMetrics()
        {
            var metricsStage = new NodeMetricsStage();
            _pipeline.Register(new SkeletonToStageAdapter<WfPipelineContext>(metricsStage));
            return this;
        }

        public NodeProcessPipelineBuilder AddStage(IPipelineStage<WfPipelineContext> stage)
        {
            ArgumentNullException.ThrowIfNull(stage);
            _pipeline.Register(stage);
            return this;
        }

        public NodeProcessPipelineBuilder AddPipeline(IPipeline<WfPipelineContext, WfPipelineContext> pipeline)
        {
            ArgumentNullException.ThrowIfNull(pipeline);
            _pipeline.Register(new PipelineToStageAdapter(pipeline));
            return this;
        }


        public NodeProcessPipelineBuilder AddAllStages(
            GraphFoundation.Abstractions.IGraph<string, NodeConfig, object> graph,
            SessionFactory? sessionFactory = null,
            INodeChainService? chainService = null,
            IWorkflowRuntimeContextAccessor? contextAccessor = null,
            IExecutionStateStore<SMExecutionStatus>? stateStore = null,
            Share.StateManagement.INodeExecutionCoordinator? coordinator = null,
            INodeProcessor<string, NodeConfig, object, WfPipelineContext>? nodeProcessor = null,
            bool enableErrorHandling = true)
        {
            return this
                .AddGraphDefinition(graph, sessionFactory)
                .AddNodeChain(chainService, contextAccessor)
                .AddNodeExtractor()
                .AddGraphChainUpdate()
                .AddNodeExecution(stateStore, coordinator, enableErrorHandling)
                .AddNodeLoop(nodeProcessor)
                .AddNodeMetrics();
        }


        public StagePipeline<WfPipelineContext> Build()
        {
            return _pipeline;
        }


        private sealed class PipelineToStageAdapter : PipelineStageBase<WfPipelineContext>
        {
            private readonly IPipeline<WfPipelineContext, WfPipelineContext> _pipeline;

            public PipelineToStageAdapter(
                IPipeline<WfPipelineContext, WfPipelineContext> pipeline,
                string? name = null,
                string? description = null)
                : base(name ?? pipeline.Name, description ?? $"{pipeline.Name} (adapted from Pipeline)")
            {
                _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            }

            protected override async Task ExecuteCoreAsync(WfPipelineContext context, CancellationToken cancellationToken)
            {
                var pipelineCtx = new PipelineContext(cancellationToken);
                await _pipeline.ExecuteAsync(context, pipelineCtx).ConfigureAwait(false);
            }
        }
    }
}
