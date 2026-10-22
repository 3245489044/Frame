using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Share.AgentLoop;

/// <summary>工具循环消息（role/content + 工具调用回填）。</summary>
public sealed record ToolLoopMessage(
    string Role,
    string Content,
    IReadOnlyList<ToolLoopCall>? ToolCalls = null,
    string? ToolCallId = null);

/// <summary>LLM 决定的单次工具调用。</summary>
public sealed record ToolLoopCall(string Name, string Arguments, string? Id = null);

/// <summary>可用工具定义（执行经宿主委托）。</summary>
public sealed record ToolLoopTool(string Name, string Description, string? InputSchema = null);

/// <summary>单次 LLM 往返结果。</summary>
public sealed record ToolLoopReply(
    string? Content,
    bool IsSuccess = true,
    string? Error = null,
    IReadOnlyList<ToolLoopCall>? ToolCalls = null,
    int? Usage = null);

/// <summary>工具执行结果。</summary>
public sealed record ToolOutcome(bool Ok, string? Text, string? Error = null);

/// <summary>工具多轮循环终态。</summary>
public sealed record ToolLoopResult(
    bool Ok,
    string Text,
    string? Error,
    IReadOnlyList<ToolLoopCall> Executed,
    IReadOnlyList<ToolLoopCall> Pending);

/// <summary>
/// 通用工具多轮循环内核：发一次 → 有 tool_calls 则逐条执行并回填 → 直至无工具调用或超限。
/// <para>客户端与工具执行均为注入委托，框架不依赖任何 Agent 执行器。</para>
/// </summary>
public sealed class ToolLoopKernel
{
    public delegate Task<ToolLoopReply> ChatOnce(
        IReadOnlyList<ToolLoopMessage> messages,
        IReadOnlyList<ToolLoopTool> tools,
        CancellationToken ct);

    public delegate Task<ToolOutcome> ExecuteTool(ToolLoopCall call, CancellationToken ct);

    private readonly ChatOnce _chat;
    private readonly ExecuteTool _execute;
    private readonly int _maxIterations;

    public ToolLoopKernel(ChatOnce chat, ExecuteTool execute, int maxIterations = 8)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _maxIterations = maxIterations > 0 ? maxIterations : 8;
    }

    public async Task<ToolLoopResult> RunAsync(
        string system, string user, IReadOnlyList<ToolLoopTool>? tools, CancellationToken ct = default)
    {
        var messages = new List<ToolLoopMessage>();
        if (!string.IsNullOrWhiteSpace(system)) messages.Add(new ToolLoopMessage("system", system));
        if (!string.IsNullOrWhiteSpace(user)) messages.Add(new ToolLoopMessage("user", user));

        var toolList = tools ?? Array.Empty<ToolLoopTool>();
        var executed = new List<ToolLoopCall>();

        for (var iteration = 0; iteration < _maxIterations; iteration++)
        {
            var reply = await _chat(messages, toolList, ct).ConfigureAwait(false);
            if (reply is null) return new ToolLoopResult(false, string.Empty, "LLM 调用返回空结果。", executed, Array.Empty<ToolLoopCall>());
            if (!reply.IsSuccess) return new ToolLoopResult(false, string.Empty, reply.Error ?? "LLM 调用失败。", executed, Array.Empty<ToolLoopCall>());

            var calls = reply.ToolCalls?.Where(static c => c is not null).ToList();
            if (calls is not { Count: > 0 })
                return new ToolLoopResult(true, reply.Content ?? string.Empty, null, executed, Array.Empty<ToolLoopCall>());

            messages.Add(new ToolLoopMessage("assistant", reply.Content ?? string.Empty, calls));
            foreach (var call in calls)
            {
                var outcome = await _execute(call, ct).ConfigureAwait(false);
                executed.Add(call);
                var text = outcome.Ok ? outcome.Text ?? string.Empty : outcome.Error ?? "tool_failed";
                messages.Add(new ToolLoopMessage("tool", text, ToolCallId: call.Id));
            }
        }

        return new ToolLoopResult(
            false, string.Empty, $"工具循环超过上限 {_maxIterations} 轮。",
            executed, Array.Empty<ToolLoopCall>());
    }
}
