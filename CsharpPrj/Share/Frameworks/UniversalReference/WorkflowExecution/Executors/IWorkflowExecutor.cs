using System.Collections.Generic;
using System.Threading.Tasks;
using UniversalWorkflowFramework.Core.Models;

namespace UniversalWorkflowFramework.WorkflowExecution.Executors
{
    public interface IWorkflowExecutor
    {
        Task InitializeAsync();

        Task<ExecutionResult> ExecuteAsync(object action, bool isUIAction);

        Task<ExecutionResult> ExecuteBranchAsync(string branchId, Dictionary<string, object> parameters);

        Task<ValidationResult> ValidateExecutionAsync(string workflowId, params ExecutionResult[] executionResults);
    }
}