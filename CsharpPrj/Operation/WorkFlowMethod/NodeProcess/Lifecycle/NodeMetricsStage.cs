﻿#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Pipeline;
using SkeletonFlow;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.Diagnostics;
using WorkFlowMethod.Execution.Session;
#endregion

using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;

namespace WorkFlowMethod.Skeletons.NodeProcess.Lifecycle
{
    public class NodeExecutionRecord
    {
        public string NodeKey { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class NodeMetricsStage : SkeletonBase<WfPipelineContext, WfPipelineContext>
    {
        public NodeMetricsStage()
            : base("NodeMetricsStage", "收集节点执行指标数据")
        {
        }

        protected override WfPipelineContext ExecuteCore(WfPipelineContext input, SkeletonContext context)
        {
            throw new NotSupportedException(
                $"NodeMetricsStage 不支持同步执行。" +
                $"请使用异步路径 ExecuteCoreAsync。");
        }

        protected override async Task<WfPipelineContext> ExecuteCoreAsync(WfPipelineContext input, SkeletonContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var session = input.GetSession<ExecutionSession>();
            if (session == null) return input;

            var totalNodes = session.TotalNodeCount;
            var completedNodes = session.CompletedCount;
            var failedNodes = session.FailedCount;
            var duration = DateTime.UtcNow - session.CreatedAt;

            session.Data?.Set("TotalNodes", totalNodes);
            session.Data?.Set("CompletedNodes", completedNodes);
            session.Data?.Set("FailedNodes", failedNodes);
            session.Data?.Set("TotalDuration", duration);

            WorkFlowMethodLog.Info("NodeRecord",
                $"节点执行汇总: 共 {totalNodes} 节点, 完成 {completedNodes}, " +
                $"{failedNodes} 失败, 耗时 {duration.TotalSeconds:F2}s");

            input.SetData("Metrics_CompletedNodes", completedNodes);
            input.SetData("Metrics_FailedNodes", failedNodes);
            input.SetData("Metrics_TotalDuration", duration);
            input.SetData("Metrics_TotalNodeCount", totalNodes);

            WorkFlowMethodLog.Info("NodeMetrics",
                $"指标收集完成: 完成={completedNodes}, 失败={failedNodes}, " +
                $"耗时={duration}");

            return input;
        }
    }
}

