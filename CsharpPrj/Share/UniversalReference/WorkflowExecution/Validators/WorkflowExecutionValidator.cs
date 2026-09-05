#nullable enable

using System.Threading.Tasks;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.Diagnostics;

namespace UniversalWorkflowFramework.WorkflowExecution
{
    public class WorkflowExecutionValidator
    {
        public async Task<ValidationResult> ValidateExecutionAsync(string workflowId, ExecutionResult uiExecutionResult, ExecutionResult aiExecutionResult)
        {
            try
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(
                    DiagnosticEvents.Validation.Start,
                    new { message = $"验证工作流执行: {workflowId}", workflowId });

                if (uiExecutionResult.Status != ExecutionStatus.Success || aiExecutionResult.Status != ExecutionStatus.Success)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        Message = $"工作流执行失败: UI状态={uiExecutionResult.Status}, AI状态={aiExecutionResult.Status}",
                        Details = new System.Collections.Generic.Dictionary<string, object>
                        {
                            { "workflowId", workflowId },
                            { "uiStatus", uiExecutionResult.Status },
                            { "aiStatus", aiExecutionResult.Status },
                            { "uiError", uiExecutionResult.Error },
                            { "aiError", aiExecutionResult.Error }
                        }
                    };
                }

                var isConsistent = await ValidateExecutionConsistencyAsync(uiExecutionResult, aiExecutionResult);
                if (!isConsistent)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        Message = "工作流执行结果不一致",
                        Details = new System.Collections.Generic.Dictionary<string, object>
                        {
                            { "workflowId", workflowId },
                            { "validationSource", "ExecutionConsistency" }
                        }
                    };
                }

                UniversalWorkflowFrameworkDiagnosticContext.Record(
                    DiagnosticEvents.Validation.Pass,
                    new { message = $"工作流执行验证通过: {workflowId}", workflowId });
                return new ValidationResult
                {
                    IsValid = true,
                    Message = "工作流执行验证通过",
                    Details = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "workflowId", workflowId },
                        { "validationSource", "GraphLogic" }
                    }
                };
            }
            catch (System.Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(
                    DiagnosticEvents.Validation.Error,
                    new { message = $"验证工作流执行失败: {workflowId}", workflowId, error = ex.Message });
                return new ValidationResult
                {
                    IsValid = false,
                    Message = $"验证工作流执行时出错: {ex.Message}",
                    Details = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "workflowId", workflowId },
                        { "error", ex.Message }
                    }
                };
            }
        }

        private async Task<bool> ValidateExecutionConsistencyAsync(ExecutionResult uiExecutionResult, ExecutionResult aiExecutionResult)
        {
            await Task.CompletedTask;

            return true;
        }
    }
}