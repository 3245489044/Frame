
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
using Operation.WorkFlowMethod.Nodes.Contracts;

using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;
using SMExecutionStatus = Share.StateManagement.ExecutionStatus;

namespace WorkFlowMethod.Skeletons.NodeProcess
{
    public class NodeProcessPipelineBuilder
    {
        private readonly StagePipeline<WfPipelineContext> _pipeline;
        private INodeOutputStore? _outputStore;
        private NodePersistCoordinator? _persistCoordinator;

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
            int maxIterations = 10000,
            Share.StateManagement.INodeExecutor? executor = null)
        {
            var stepSkeleton = new NodeExecutionStepSkeleton(
                nodeProcessor != null ? new NodeChainSkeleton() : null,
                executor);

            var stepPipeline = PipelineUsage.CreateStage<WfPipelineContext>(
                name: "NodeLoopStep",
                action: (ctx, pipelineCtx) =>
                {
                    var skeletonCtx = new SkeletonContext();
                    stepSkeleton.ExecuteAsync(ctx, skeletonCtx).GetAwaiter().GetResult();

                    // 节点即存储单元：注入 store 时，单节点执行后持久化当前节点产出端口（未注入 → 零回归）
                    if (_outputStore != null)
                    {
                        NodeOutputPersistStage.PersistAsync(ctx, _outputStore, pipelineCtx.CancellationToken, _persistCoordinator)
                            .GetAwaiter().GetResult();

                        // S4 入参读源：将刚持久化的当前节点输出端口水化进参数解析器，
                        // 使下游节点经 MergeParameters 可按 {port} 或 {nodeKey}.{port} 取用。
                        NodeParameterHydrator.HydrateCurrentNodeAsync(ctx, _outputStore, pipelineCtx.CancellationToken)
                            .GetAwaiter().GetResult();
                    }
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

        /// <summary>
        /// 装配「节点即存储单元」：注册节点输出存储；NodeLoop 循环体内单节点执行后会持久化该节点产出端口。
        /// <paramref name="coordinator"/> 非空时为同 workflowId 写入套单写门（fail-fast + 有界等待）。
        /// 请在 <see cref="AddNodeLoop"/>（或 <see cref="AddAllStages"/>）之前调用；未装配（store 为 null）→ 管线零回归。
        /// </summary>
        public NodeProcessPipelineBuilder AddNodeOutputStore(
            INodeOutputStore? store,
            NodePersistCoordinator? coordinator = null)
        {
            _outputStore = store;
            _persistCoordinator = coordinator;
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
            bool enableErrorHandling = true,
            Share.StateManagement.INodeExecutor? executor = null)
        {
            return this
                .AddGraphDefinition(graph, sessionFactory)
                .AddNodeChain(chainService, contextAccessor)
                .AddNodeExtractor()
                .AddGraphChainUpdate()
                .AddNodeExecution(stateStore, coordinator, enableErrorHandling)
                .AddNodeLoop(nodeProcessor, executor: executor)
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
