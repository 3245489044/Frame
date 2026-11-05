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
        var k = kind.Trim().ToLowerInvariant();

        // 已知映射必须先于下划线透传判定，否则 "tool_success"/"tool_failure" 会被原样返回（永不归一）。
        switch (k)
        {
            case "externalinput":
            case "external_input":
                return ExternalInput;
            case "subagentcompleted":
            case "subagent_completed":
            case "done":
            case "error":
            case "retry":
                return SubAgentCompleted;
            case "nodeproduced":
            case "node_produced":
                return NodeProduced;
            case "toolresult":
            case "tool_success":
            case "toolsucceeded":
            case "tool_failure":
            case "toolfailed":
            case "tooltimedout":
                return ToolResult;
            case "taskstate":
            case "taskstatus":
            case "task_state":
                return TaskState;
        }

        return k.Contains('_') ? k : Unknown;
    }
}
