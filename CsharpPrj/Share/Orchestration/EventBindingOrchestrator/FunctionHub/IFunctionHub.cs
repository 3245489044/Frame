
using System.Collections.Generic;
using TConfig;

namespace Share.EventBindingOrchestrator.FunctionHub
{
    public interface IFunctionHub
    {
        void RegisterWorkflows(Dictionary<string, WorkflowDefinitionEntry>? workflows);

        WorkflowExecutionPlan? Resolve(string functionName);

        /// <summary>
        /// 结构化解析（E14）：未命中不再只返回 null+Warning，而是给出可判定的失败原因
        /// （InvalidName / NotRegistered），供调用方区分「名字非法」与「未注册」。
        /// </summary>
        FunctionResolution ResolveDetailed(string functionName);

        bool HasFunction(string functionName);

        IEnumerable<string> GetWorkflowNames();
    }

    /// <summary>函数解析状态（结构化失败词表）。</summary>
    public enum FunctionResolveStatus
    {
        /// <summary>解析成功（工作流字典命中，或由 ITrConfigRegistry 单函数自动包装）。</summary>
        Found,

        /// <summary>函数名为空/空白（调用方参数错误）。</summary>
        InvalidName,

        /// <summary>工作流字典与注册表均未命中（未注册）。</summary>
        NotRegistered
    }

    /// <summary>函数解析结果（Plan 非空 ⇔ Status=Found）。</summary>
    public readonly record struct FunctionResolution(
        FunctionResolveStatus Status,
        WorkflowExecutionPlan? Plan,
        string? Reason)
    {
        public bool Found => Status == FunctionResolveStatus.Found;
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