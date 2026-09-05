namespace FrontEndDriven.Abstractions;

#region === 工作流引擎 ===

public interface IWorkflowEngine
{
    Task<string> ExecuteWorkflowAsync(string workflowName, string input);

    Task<string> ExecuteWorkflowAsync(IWorkflowDefinition definition, string input);

    IWorkflowDefinition? LoadWorkflow(string filePath);

    IWorkflowDefinition? GetWorkflow(string workflowName);

    IEnumerable<string> GetAllWorkflowNames();

    void RegisterWorkflow(IWorkflowDefinition definition);

    void ClearCache();
}

#endregion

#region === 工作流定义 ===

public interface IWorkflowDefinition
{
    string WorkflowName { get; }

    string DisplayName { get; }

    string Description { get; }

    string Version { get; }

    IReadOnlyList<IWorkflowStep> Steps { get; }

    IReadOnlyDictionary<string, string> OutputMapping { get; }
}

#endregion

#region === 工作流步骤 ===

public interface IWorkflowStep
{
    string StepName { get; }

    string FuncName { get; }

    IReadOnlyList<string> InputKeys { get; }

    string OutputKey { get; }

    IReadOnlyList<string> Parameters { get; }
}

#endregion
