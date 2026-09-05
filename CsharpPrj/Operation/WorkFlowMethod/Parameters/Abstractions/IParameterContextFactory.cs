
using WorkflowExecution.Execution;

namespace WorkFlowMethod.Parameters.Abstractions;

public interface IParameterContextFactory
{
    Orchestration.Context.Models.WorkflowContext CreateWorkflowContext(
        Dictionary<string, object>? parameters = null,
        Dictionary<string, object>? data = null,
        string? executionId = null,
        string? nodeKey = null,
        string? workflowId = null);

    FuncExecutionContext CreateFuncExecutionContext(
        Dictionary<string, object>? parameters = null,
        Dictionary<string, object>? data = null,
        string? executionId = null,
        string? nodeKey = null,
        string? workflowId = null);

    FuncExecutionContext CreateCallContext(
        FuncCall call,
        FuncExecutionContext baseContext,
        object? recursiveResult = null);
}
