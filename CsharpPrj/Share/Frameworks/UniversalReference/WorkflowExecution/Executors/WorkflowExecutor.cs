using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Modules.Pipeline;
using UniversalWorkflowFramework.ActionMapping.Models;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.Diagnostics;


namespace UniversalWorkflowFramework.WorkflowExecution.Executors
{
    /// <summary>
    /// 工作流执行器。
    /// 基于 Modules.Pipeline（Share.Pipeline）编排执行/验证流程：
    /// 用 IPipeline + WithLifecycle/WithRetryPolicy 组合子收敛日志与异常样板。
    /// 公共 API（IWorkflowExecutor / WorkflowExecutorFactory）保持不变。
    /// </summary>
    public class WorkflowExecutor : IWorkflowExecutor
    {
        private const string ResourceTypeAction = "action";
        private const string OutputKeyExecutionSource = "execution_source";
        private const string OutputKeyActionType = "action_type";
        private const string OutputKeyExecutionTime = "execution_time";
        private const string ExecutionSourceUI = "UI";
        private const string ExecutionSourceAI = "AI";
        private const int SimulationDelayMs = 100;
        private const int DefaultExecutionTimeMs = 150;

        private readonly IUniversalReferenceManager _referenceManager;

        private readonly IPipeline<WorkflowContext, WorkflowContext> _executePipeline;
        private readonly IPipeline<WorkflowContext, WorkflowContext> _validatePipeline;

        public WorkflowExecutor(IUniversalReferenceManager referenceManager)
        {
            _referenceManager = referenceManager ?? throw new ArgumentNullException(nameof(referenceManager));
            _executePipeline = BuildExecutePipeline();
            _validatePipeline = BuildValidatePipeline();
        }

        public WorkflowExecutor()
            : this(UniversalReferenceManagerFactory.Create())
        {
        }

        public async Task InitializeAsync()
        {
            try
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Executor.Init, new { message = "初始化工作流执行器" });
                await _referenceManager.InitializeAsync();
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Executor.InitSuccess, new { message = "工作流执行器初始化成功" });
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Executor.InitError, new { message = "初始化工作流执行器失败", error = ex.Message });
                throw;
            }
        }

        public async Task<ExecutionResult> ExecuteAsync(object action, bool isUIAction)
        {
            var context = new WorkflowContext { Action = action, IsUIAction = isUIAction };
            return await RunExecutePipelineAsync(context);
        }

        public async Task<ExecutionResult> ExecuteBranchAsync(string branchId, Dictionary<string, object> parameters)
        {
            var context = new WorkflowContext { BranchId = branchId, Parameters = parameters };
            return await RunExecutePipelineAsync(context);
        }

        public async Task<ValidationResult> ValidateExecutionAsync(string workflowId, params ExecutionResult[] executionResults)
        {
            var context = new WorkflowContext { WorkflowId = workflowId, ExecutionResults = executionResults };

            try
            {
                var done = await _validatePipeline.ExecuteAsync(context);
                return done.Validation ?? new ValidationResult { IsValid = false, Message = "验证未产生结果" };
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Validate.Error, new { message = $"验证执行结果失败: {workflowId}", workflowId, error = ex.Message });
                return new ValidationResult { IsValid = false, Message = $"验证执行结果时出错: {ex.Message}" };
            }
        }

        private async Task<ExecutionResult> RunExecutePipelineAsync(WorkflowContext context)
        {
            try
            {
                var done = await _executePipeline.ExecuteAsync(context);
                return done.Result ?? new ExecutionResult { Status = ExecutionStatus.Failed, Error = "执行未产生结果" };
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Executor.Error, new { message = "执行操作失败", error = ex.Message });
                return new ExecutionResult { Status = ExecutionStatus.Failed, Error = ex.Message };
            }
        }

        /// <summary>执行阶段：解析操作/分支并产出 ExecutionResult（失败也落盘到 ctx.Result，不抛异常）。</summary>
        private async Task ExecuteCoreAsync(WorkflowContext ctx)
        {
            if (!string.IsNullOrEmpty(ctx.BranchId))
            {
                await Task.Delay(SimulationDelayMs * 2);

                var branchResult = new ExecutionResult
                {
                    Status = ExecutionStatus.Success,
                    Output = new Dictionary<string, object>(ctx.Parameters ?? new Dictionary<string, object>()),
                    ExecutionTime = TimeSpan.FromMilliseconds(DefaultExecutionTimeMs * 2)
                };
                branchResult.Output["branchId"] = ctx.BranchId;
                branchResult.Output["executionTime"] = DateTime.UtcNow;
                ctx.Result = branchResult;
                return;
            }

            if (!TryExtractAction(ctx.Action, ctx.IsUIAction, out var actionType, out var parameters))
                throw new ArgumentException("无效的操作类型");

            var resourceId = new ResourceIdentifier { ResourceType = ResourceTypeAction, Id = actionType };
            var actionResource = await _referenceManager.GetResourceAsync(resourceId);
            if (actionResource == null)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Executor.ActionNotFound, new { message = $"未找到操作逻辑: {actionType}", actionType });
                ctx.Result = new ExecutionResult
                {
                    Status = ExecutionStatus.Failed,
                    Error = $"未找到操作逻辑: {actionType}"
                };
                return;
            }

            await Task.Delay(SimulationDelayMs);

            var result = new ExecutionResult
            {
                Status = ExecutionStatus.Success,
                Output = new Dictionary<string, object>(parameters),
                ExecutionTime = TimeSpan.FromMilliseconds(DefaultExecutionTimeMs)
            };
            result.Output[OutputKeyExecutionSource] = ctx.IsUIAction ? ExecutionSourceUI : ExecutionSourceAI;
            result.Output[OutputKeyActionType] = actionType;
            result.Output[OutputKeyExecutionTime] = DateTime.UtcNow;
            ctx.Result = result;
        }

        /// <summary>验证阶段：逐结果校验状态与多结果一致性。</summary>
        private void ValidateCore(WorkflowContext ctx)
        {
            var results = ctx.ExecutionResults;
            if (results == null || results.Count == 0)
            {
                ctx.Validation = new ValidationResult { IsValid = false, Message = "没有可验证的执行结果" };
                return;
            }

            var workflowId = ctx.WorkflowId ?? string.Empty;

            foreach (var result in results)
            {
                if (result.Status != ExecutionStatus.Success)
                {
                    ctx.Validation = new ValidationResult
                    {
                        IsValid = false,
                        Message = $"执行失败: {result.Error}",
                        Details = new Dictionary<string, object>
                        {
                            { "workflowId", workflowId },
                            { "status", result.Status.ToString() },
                            { "error", result.Error }
                        }
                    };
                    return;
                }
            }

            if (results.Count > 1)
            {
                var firstOutput = results[0].Output;
                for (int i = 1; i < results.Count; i++)
                {
                    var currentOutput = results[i].Output;
                    if (firstOutput == null || currentOutput == null)
                    {
                        SetInconsistentResult(ctx, workflowId);
                        return;
                    }

                    foreach (var key in firstOutput.Keys)
                    {
                        if (!currentOutput.ContainsKey(key) || !firstOutput[key].Equals(currentOutput[key]))
                        {
                            SetInconsistentResult(ctx, workflowId);
                            return;
                        }
                    }
                }
            }

            ctx.Validation = new ValidationResult
            {
                IsValid = true,
                Message = $"执行结果验证通过: {workflowId}",
                Details = new Dictionary<string, object>
                {
                    { "workflowId", workflowId },
                    { "executionCount", results.Count },
                    { "validationTime", DateTime.UtcNow }
                }
            };
        }

        private static void SetInconsistentResult(WorkflowContext ctx, string workflowId)
        {
            ctx.Validation = new ValidationResult
            {
                IsValid = false,
                Message = "执行结果不一致",
                Details = new Dictionary<string, object>
                {
                    { "workflowId", workflowId },
                    { "validationSource", "ExecutionConsistency" }
                }
            };
        }


        private IPipeline<WorkflowContext, WorkflowContext> BuildExecutePipeline()
        {
            var execute = PipelineUsage.Create<WorkflowContext, WorkflowContext>(
                "Execute",
                sync: ctx => throw new NotSupportedException("此执行管线仅支持异步执行（ExecuteAsync）。"),
                async: async ctx => { await ExecuteCoreAsync(ctx); return ctx; });

            return execute
                .WithLifecycle(
                    onBefore: ctx => UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Executor.Before, new { message = $"执行操作: {ctx.DisplayName}", displayName = ctx.DisplayName }),
                    onAfter: ctx => UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Executor.After, new { message = $"执行操作完成: {ctx.DisplayName}", displayName = ctx.DisplayName }),
                    onError: ex => UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Executor.Error, new { message = "执行操作失败", error = ex.Message }))
                .WithRetryPolicy(maxRetries: 1);
        }

        private IPipeline<WorkflowContext, WorkflowContext> BuildValidatePipeline()
        {
            var validate = PipelineUsage.Create<WorkflowContext, WorkflowContext>(
                "Validate",
                sync: ctx => { ValidateCore(ctx); return ctx; },
                async: async ctx => { await Task.CompletedTask; ValidateCore(ctx); return ctx; });

            return validate.WithLifecycle(
                onBefore: ctx => UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Validate.Before, new { message = $"验证执行结果: {ctx.WorkflowId}", workflowId = ctx.WorkflowId }),
                onAfter: ctx => UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Validate.After, new { message = $"验证执行结果完成: {ctx.WorkflowId}", workflowId = ctx.WorkflowId }),
                onError: ex => UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Validate.Error, new { message = "验证执行结果失败", error = ex.Message }));
        }

        private static bool TryExtractAction(
            object? action,
            bool isUIAction,
            out string actionType,
            out Dictionary<string, object> parameters)
        {
            actionType = string.Empty;
            parameters = new Dictionary<string, object>();

            if (isUIAction && action is UIAction uiAction)
            {
                actionType = uiAction.ActionType;
                parameters = uiAction.Parameters;
                return true;
            }

            if (!isUIAction && action is AIAction aiAction)
            {
                actionType = aiAction.ActionType;
                parameters = aiAction.Parameters;
                return true;
            }

            return false;
        }

        /// <summary>执行/验证流程的共享上下文。</summary>
        private sealed class WorkflowContext
        {
            public object? Action { get; init; }

            public bool IsUIAction { get; init; }

            public string? BranchId { get; init; }

            public Dictionary<string, object>? Parameters { get; init; }

            public string? WorkflowId { get; init; }

            public IReadOnlyList<ExecutionResult>? ExecutionResults { get; init; }

            public string DisplayName =>
                !string.IsNullOrEmpty(BranchId) ? BranchId! :
                Action != null ? (IsUIAction ? "UI操作" : "AI操作") : "验证";

            public ExecutionResult? Result { get; set; }

            public ValidationResult? Validation { get; set; }
        }
    }

    public static class WorkflowExecutorFactory
    {
        public static IWorkflowExecutor Create()
        {
            return new WorkflowExecutor();
        }

        public static IWorkflowExecutor Create(IUniversalReferenceManager referenceManager)
        {
            return new WorkflowExecutor(referenceManager);
        }
    }
}

