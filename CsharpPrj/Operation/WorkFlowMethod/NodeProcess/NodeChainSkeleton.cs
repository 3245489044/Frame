﻿#region USINGS
using System;
using System.Threading.Tasks;
using SkeletonFlow;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.GraphFramework.Services;
using WorkFlowMethod.Infrastructure.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using GraphFramework.Models;
using UniversalMiddleware.Chain;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess
{
    public class NodeChainSkeleton : SkeletonBase<PipelineContext, PipelineContext>
    {
        private readonly INodeChainService _chainService;

        public NodeChainSkeleton(
            INodeChainService chainService = null,
            IWorkflowRuntimeContextAccessor contextAccessor = null)
            : base("NodeChainSkeleton", "从 JSON 创建工作流节点链，注册到图快照")
        {
            _chainService = chainService ?? new NodeChainService(contextAccessor: contextAccessor);
        }

        protected override PipelineContext ExecuteCore(PipelineContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var jsonConfig = input.GetData<string>("NodeChain.JsonConfig");
            var chainCount = input.GetData<int>("NodeChain.ChainCount");
            var workflowId = input.GetData<string>("NodeChain.WorkflowId");

            if (string.IsNullOrEmpty(jsonConfig))
            {
                context.SetItem("NodeChainSkeleton.Warning", "未找到 JSON 配置（键: NodeChain.JsonConfig），跳过链创建");
                return input;
            }

            if (chainCount <= 0)
            {
                context.SetItem("NodeChainSkeleton.Warning", "链数量 <= 0（键: NodeChain.ChainCount），跳过链创建");
                return input;
            }

            try
            {
                // 通用链定义加载骨架（P0）：parse 委托 NodeChainService.CreateChain；
                // 不注入 normalize/validate（保持既有行为，不新增校验/规范化语义）。
                // CreateChain 对非法 JSON 抛异常 → 经 Load 原样上抛（行为与迁移前一致）。
                var skeleton = new ChainDefinitionLoadSkeleton<Chain<string, NodeConfig>>(
                    json => _chainService.CreateChain<NodeConfig>(json, chainCount, workflowId));

                var result = skeleton.Load(jsonConfig);

                if (result.Success && result.Definition != null)
                {
                    input.SetData("NodeChain.ChainInfo", result.Definition);

                    context.SetItem("NodeChainSkeleton.NodeCount", result.Definition.AllNodes?.Count ?? 0);
                    context.SetItem("NodeChainSkeleton.ChainCount", chainCount);
                    context.SetItem("NodeChainSkeleton.WorkflowId", workflowId ?? "default");
                    context.SetItem("NodeChainSkeleton.Status", "Success");
                }
                else
                {
                    context.SetItem("NodeChainSkeleton.Status", "Failed");
                    context.SetItem("NodeChainSkeleton.Error", result.Error ?? "链定义加载失败");
                }
            }
            catch (Exception ex)
            {
                context.SetItem("NodeChainSkeleton.Status", "Failed");
                context.SetItem("NodeChainSkeleton.Error", ex.Message);
                throw;
            }

            return input;
        }

        protected override Task<PipelineContext> ExecuteCoreAsync(
            PipelineContext input, SkeletonContext context)
        {
            return Task.FromResult(ExecuteCore(input, context));
        }
    }
}

