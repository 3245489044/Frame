
using System.Collections.Generic;
using TConfig;

namespace Share.EventBindingOrchestrator.FunctionHub
{
    public interface IFunctionHub
    {
        void RegisterFunctions(IEnumerable<FunctionRegistrationEntry>? functions);

        void RegisterWorkflows(Dictionary<string, WorkflowDefinitionEntry>? workflows);

        WorkflowExecutionPlan? Resolve(string functionName);

        bool HasFunction(string functionName);

        IEnumerable<string> GetWorkflowNames();
    }

    public class FunctionRegistrationEntry
    {
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string? Method { get; init; }
        public bool Enabled { get; init; } = true;
    }

    public class WorkflowExecutionPlan
    {
        public WorkflowDefinitionEntry WorkflowDef { get; }

        public string FunctionName => WorkflowDef.Name;

        public int NodeCount => WorkflowDef.Nodes?.Count ?? 0;

        public WorkflowExecutionPlan(WorkflowDefinitionEntry workflowDef)
        {
            WorkflowDef = workflowDef ?? throw new System.ArgumentNullException(nameof(workflowDef));
        }
    }
}