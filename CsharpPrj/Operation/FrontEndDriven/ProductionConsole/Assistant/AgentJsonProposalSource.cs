using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FrontEndDriven.Shared.Utils;
using Share.EventBindingOrchestrator.Abstractions;
using Share.EventBindingOrchestrator.Execution;

namespace FrontEndDriven.ProductionConsole.Assistant;

/// <summary>
/// 通用 JSON 提案来源（按名委托 Agent，唯一实现）：提示词装配 → <c>AgentUi_ConversationSendWire</c>
/// 一次性 answer → wire → JSON。不新增 HTTP 客户端/端点常量；未装配 Agent 时确定性返回 null。
/// </summary>
public sealed class AgentJsonProposalSource : IJsonProposalSource
{
    /// <summary>Agent 侧一次性对话函数（跨插件按名调用，不引程序集）。</summary>
    public const string FunctionName = "AgentUi_ConversationSendWire";

    /// <summary>提案专用会话名（单步、无历史依赖）。</summary>
    public const string DefaultSessionName = "ui-json-assist";

    /// <summary>默认超时（毫秒）：UI 单步可接受的最长等待。</summary>
    public const int DefaultTimeoutMs = 25_000;

    private readonly string _sessionName;
    private readonly int _timeoutMs;

    public AgentJsonProposalSource(string? sessionName = null, int timeoutMs = DefaultTimeoutMs)
    {
        _sessionName = string.IsNullOrWhiteSpace(sessionName) ? DefaultSessionName : sessionName.Trim();
        _timeoutMs = timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs;
    }

    public async Task<JsonProposal?> ProposeAsync(JsonProposalRequest request, CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Prompt)) return null;

        FunctionCallFacadeResult result;
        try
        {
            result = await FunctionCallFacade.ExecuteAsync(new FunctionCallRequest
            {
                FunctionName = FunctionName,
                Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["sessionName"] = _sessionName,
                    ["text"] = BuildPrompt(request),
                },
                Source = FunctionCallSource.ExternalPort,
                Mode = ExecutionMode.Auto,
                TimeoutMs = _timeoutMs,
                CallerToken = ct,
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Output)) return null;

        var json = WireJson.ExtractObject(result.Output);
        return json is null ? null : new JsonProposal { Json = json };
    }

    /// <summary>装配单步提示词：目标 + schema + 当前 JSON + 上次输出（可选），要求只输出一个 JSON 对象。</summary>
    public static string BuildPrompt(JsonProposalRequest request)
    {
        var sb = new StringBuilder();
        sb.Append("你是 JSON 控制台的参数助理。根据用户描述生成参数 JSON。\n");

        if (!string.IsNullOrWhiteSpace(request.TargetId))
            sb.Append("\n目标：").Append(request.TargetId).Append('\n');

        if (!string.IsNullOrWhiteSpace(request.SchemaText))
            sb.Append("\n可用键（键 / 类型 / 约束 / 示例）：\n").Append(request.SchemaText.TrimEnd()).Append('\n');

        if (!string.IsNullOrWhiteSpace(request.CurrentJson))
            sb.Append("\n当前 JSON（只覆盖你建议的键，其余保持不变）：\n")
              .Append(request.CurrentJson.Trim()).Append('\n');

        if (!string.IsNullOrWhiteSpace(request.LastOutput))
            sb.Append("\n上次执行输出（仅供修正，不要复述）：\n")
              .Append(Truncate(request.LastOutput!, 800)).Append('\n');

        sb.Append("\n用户描述：").Append(request.Prompt.Trim()).Append('\n');
        sb.Append("\n要求：只输出一个 JSON 对象；键必须来自可用键；值满足类型与约束；不要输出解释、注释或代码围栏。");
        return sb.ToString();
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text.Substring(text.Length - max);
}

/// <summary>
/// JSON 助理运行时槽（宿主装配期注入来源；未注入时使用按名委托 Agent 的缺省来源）。
/// <para>唯一可变状态 = 来源与助理实例，宿主装配一次。</para>
/// </summary>
public static class JsonAssistRuntime
{
    private static readonly object Gate = new();
    private static IJsonProposalSource? _source;
    private static JsonStepAssistant? _assistant;

    /// <summary>宿主装配期覆盖来源（null = 回到缺省按名委托来源）。</summary>
    public static void Configure(IJsonProposalSource? source)
    {
        lock (Gate)
        {
            _source = source;
            _assistant = null;
        }
    }

    /// <summary>当前助理实例（缺省来源惰性创建；测试可直接 Configure 注入桩）。</summary>
    public static JsonStepAssistant Assistant
    {
        get
        {
            lock (Gate)
            {
                _source ??= new AgentJsonProposalSource();
                return _assistant ??= new JsonStepAssistant(_source);
            }
        }
    }
}
