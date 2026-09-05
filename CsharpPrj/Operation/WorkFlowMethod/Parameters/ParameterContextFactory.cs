
using WorkFlowMethod.Orchestration.Context.Models;
using WorkFlowMethod.Parameters.Abstractions;
using WorkflowExecution.Execution;

namespace WorkFlowMethod.Parameters;

public class ParameterContextFactory : IParameterContextFactory
{
    public static readonly ParameterContextFactory Default = new();

    public WorkflowContext CreateWorkflowContext(
        Dictionary<string, object>? parameters = null,
        Dictionary<string, object>? data = null,
        string? executionId = null,
        string? nodeKey = null,
        string? workflowId = null)
    {
        return new WorkflowContext(
            parameters: parameters,
            data: data,
            executionId: executionId,
            nodeKey: nodeKey,
            workflowId: workflowId);
    }

    public FuncExecutionContext CreateFuncExecutionContext(
        Dictionary<string, object>? parameters = null,
        Dictionary<string, object>? data = null,
        string? executionId = null,
        string? nodeKey = null,
        string? workflowId = null)
    {
        return new FuncExecutionContext(
            parameters: parameters,
            data: data,
            executionId: executionId,
            nodeKey: nodeKey,
            workflowId: workflowId);
    }

    public FuncExecutionContext CreateCallContext(
        FuncCall call,
        FuncExecutionContext baseContext,
        object? recursiveResult = null)
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
            workflowId: baseContext.WorkflowId);
    }
}
