
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorkflowExecution.Execution
{
    public class FuncCall
    {
        public string Symbol { get; set; } = string.Empty;
        public string FuncName { get; set; } = string.Empty;
        public string ParameterType { get; set; } = string.Empty;
        public List<object> Parameters { get; set; } = new();
        public FuncPipelineParameters? PipelineGroup { get; set; }

        /// <summary>统一执行单元分类（W-1/W-2）：internal/external/composition 决定分派面；默认 internal（既有 fn= 语义）。</summary>
        public FuncCallKind Kind { get; set; } = FuncCallKind.Internal;
    }

    public class FuncPipelineParameters
    {
        public List<FuncCall> Calls { get; set; } = new();
    }

    public class FuncExecutionContext
    {
        public Dictionary<string, object> Parameters { get; }
        public Dictionary<string, object> Data { get; }
        public string? ExecutionId { get; }
        public string? NodeKey { get; }
        public string? WorkflowId { get; }

        public FuncExecutionContext(
            Dictionary<string, object>? parameters = null,
            Dictionary<string, object>? data = null,
            string? executionId = null,
            string? nodeKey = null,
            string? workflowId = null)
        {
            Parameters = parameters ?? new Dictionary<string, object>();
            Data = data ?? new Dictionary<string, object>();
            ExecutionId = executionId;
            NodeKey = nodeKey;
            WorkflowId = workflowId;
        }
    }

    public interface IWorkflowRegistry
    {
        bool HasFunction(string funcName);
        object? Call(string funcName, FuncExecutionContext context);
    }

    public class WorkflowFunctionNotFoundException : Exception
    {
        public WorkflowFunctionNotFoundException(string funcName)
            : base($"工作流函数未找到: {funcName}") { }
    }

    public static class FuncCallRun
    {
        private const int MaxFunctionRecursionDepth = 32;

        /// <summary>既有重载（向后兼容）：仅持 <see cref="IWorkflowRegistry"/>（全部按 internal 分派），
        /// 内部委托到 <see cref="IFuncCallDispatcher"/> 重载（装配默认分派器，语义与旧实现逐字一致）。</summary>
        public static Task<List<object?>> PipelineRunAsync(
            FuncPipelineParameters funcPipeline,
            IWorkflowRegistry functionRegistry,
            FuncExecutionContext context,
            int depth = 0)
        {
            if (functionRegistry == null)
                throw new ArgumentNullException(nameof(functionRegistry), "函数注册器不能为 null");
            return PipelineRunAsync(funcPipeline, new WorkflowFuncCallDispatcher(functionRegistry, null), context, depth);
        }

        /// <summary>W-3 单源分派重载：每个 FuncCall 经 <see cref="IFuncCallDispatcher"/> 分派（internal/external 由 kind 决定），
        /// <c>PipelineGroup</c> 子管线仍由本管线递归承接（composition）。既有 registry 重载委托本重载（语义不变）。</summary>
        public static async Task<List<object?>> PipelineRunAsync(
            FuncPipelineParameters funcPipeline,
            IFuncCallDispatcher dispatcher,
            FuncExecutionContext context,
            int depth = 0)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher), "分派器不能为 null");
            if (context == null)
                throw new ArgumentNullException(nameof(context), "执行上下文不能为 null");

            if (depth > MaxFunctionRecursionDepth)
                throw new InvalidOperationException(
                    $"函数管线递归深度超过最大限制 ({MaxFunctionRecursionDepth})，可能存在循环引用");

            if (funcPipeline?.Calls == null) return new List<object?>();

            var result = new List<object?>();
            foreach (var call in funcPipeline.Calls)
            {
                if (string.IsNullOrEmpty(call.FuncName)) continue;

                object? parameter = null;
                if (call.PipelineGroup != null)
                {
                    parameter = await PipelineRunAsync(call.PipelineGroup, dispatcher, context, depth + 1);
                }

                var callContext = CreateCallContext(call, context, recursiveResult: parameter);

                FuncCallResult dispatched;
                try
                {
                    dispatched = dispatcher.Dispatch(call, callContext, call.Kind);
                }
                catch (WorkflowFunctionNotFoundException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new Exception($"函数执行失败: {call.FuncName}: {ex.Message}", ex);
                }

                if (!dispatched.Ok)
                    throw new Exception($"函数执行失败: {call.FuncName}: {dispatched.Error}");

                parameter = dispatched.Value;
                result.Add(parameter);
            }
            return result;
        }

        private static FuncExecutionContext CreateCallContext(FuncCall call, FuncExecutionContext baseContext, object? recursiveResult = null)
        {
            var parameters = new Dictionary<string, object>();
            if (call.Parameters != null)
            {
                for (int i = 0; i < call.Parameters.Count; i++)
                    parameters[i.ToString()] = call.Parameters[i];
            }

            if (recursiveResult != null)
            {
                var nextIndex = call.Parameters?.Count ?? 0;
                parameters[nextIndex.ToString()] = recursiveResult;
            }

            var dataCopy = baseContext.Data != null
                ? new Dictionary<string, object>(baseContext.Data)
                : new Dictionary<string, object>();

            return new FuncExecutionContext(
                parameters: parameters,
                data: dataCopy,
                executionId: baseContext.ExecutionId,
                nodeKey: baseContext.NodeKey,
                workflowId: baseContext.WorkflowId
            );
        }
    }
}
