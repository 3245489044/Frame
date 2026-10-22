using System;

namespace Share.Signal;

/// <summary>
/// 规范信号种类（与 <c>Config/nodes/signal.ushar</c> 的 <c>[Space] SignalKind</c> 同名）。
/// <para>5 套信号面的历史命名在此**单点归一**（PascalCase → snake_case）；未知 ⇒ unknown。</para>
/// </summary>
public static class SignalKinds
{
    /// <summary>外部输入。</summary>
    public const string ExternalInput = "external_input";

    /// <summary>子智能体/步骤完成。</summary>
    public const string SubAgentCompleted = "subagent_completed";

    /// <summary>节点产出。</summary>
    public const string NodeProduced = "node_produced";

    /// <summary>工具结果。</summary>
    public const string ToolResult = "tool_result";

    /// <summary>任务状态迁移。</summary>
    public const string TaskState = "task_state";

    /// <summary>未知（→ Failed(parse_error)）。</summary>
    public const string Unknown = "unknown";

    /// <summary>历史/Pascal 命名 → 规范种类（单点归一）。</summary>
    public static string Normalize(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return Unknown;
        var k = kind.Trim();
        if (k.Contains('_')) return k.ToLowerInvariant();
        return k.ToLowerInvariant() switch
        {
            "externalinput" => ExternalInput,
            "subagentcompleted" => SubAgentCompleted,
            "nodeproduced" => NodeProduced,
            "toolresult" => ToolResult,
            "tool_success" or "toolsucceeded" => ToolResult,
            "tool_failure" or "toolfailed" or "tooltimedout" => ToolResult,
            "taskstate" or "taskstatus" => TaskState,
            "done" or "error" or "retry" => SubAgentCompleted,
            _ => Unknown,
        };
    }
}
