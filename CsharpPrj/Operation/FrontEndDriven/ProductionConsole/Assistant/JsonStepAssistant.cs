using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FrontEndDriven.ProductionConsole.Assistant;

/// <summary>
/// JSON 单步助理（唯一状态机）：一次提案只产出/合并一个 JSON，应用时只覆盖被建议键。
/// <para>目标切换即失效；未接入来源/超时/解析失败/校验失败一律折叠为可见错误，不伪装成功。</para>
/// </summary>
public sealed class JsonStepAssistant
{
    private readonly IJsonProposalSource? _source;
    private readonly JsonAssistState _state = new();

    public JsonStepAssistant(IJsonProposalSource? source = null)
    {
        _source = source;
    }

    /// <summary>是否已接入提案来源（false → 生成按钮禁用并给出接入提示）。</summary>
    public bool IsAvailable => _source is not null;

    /// <summary>目标切换/表单重置 → 清空提案态（唯一失效点）。</summary>
    public void Invalidate(string? targetId)
    {
        _state.TargetId = targetId;
        _state.Prompt = null;
        _state.PatchJson = null;
        _state.ProposedJson = null;
        _state.Warnings = Array.Empty<string>();
        _state.Error = null;
        _state.AppliedCount = 0;
    }

    /// <summary>生成单步提案：来源 JSON → 按目标约束过滤/归一 → 合并进当前 JSON；失败不改当前值。</summary>
    public async Task<bool> ProposeAsync(JsonProposalRequest request, CancellationToken ct = default)
    {
        _state.TargetId = request.TargetId;
        _state.AppliedCount = 0;
        _state.PatchJson = null;
        _state.ProposedJson = null;
        _state.Warnings = Array.Empty<string>();
        _state.Error = null;

        var prompt = request.Prompt?.Trim();
        _state.Prompt = prompt;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            _state.Error = "请输入一句自然语言描述再解析";
            return false;
        }

        if (_source is null)
        {
            _state.Error = "AI 参数助理未接入（未提供提案来源）";
            return false;
        }

        JsonProposal? proposal;
        try
        {
            proposal = await _source.ProposeAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _state.Error = $"AI 解析异常: {ex.Message}";
            return false;
        }

        if (proposal is null || string.IsNullOrWhiteSpace(proposal.Json))
        {
            _state.Error = "AI 未返回可用 JSON（可能超时或解析失败），请重试或手动填写";
            return false;
        }

        var merge = JsonProposalPipeline.MergeInto(request.CurrentJson, proposal.Json, request.TemplateJson);
        if (!merge.IsSuccess)
        {
            _state.Error = "提案不可用: " + merge.Error;
            return false;
        }

        _state.PatchJson = proposal.Json;
        _state.ProposedJson = merge.Json;
        _state.Warnings = merge.Warnings;
        return true;
    }

    /// <summary>
    /// 应用 pending：把提案补丁重新合并到**当前最新** JSON（保留提案后的人工编辑），只覆盖被建议键。
    /// 目标不一致/无 pending → null。
    /// </summary>
    public JsonApplyResult? ApplyTo(string? targetId, string? currentJson, string? templateJson)
    {
        if (!string.Equals(targetId, _state.TargetId, StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.IsNullOrWhiteSpace(_state.PatchJson)) return null;

        var merge = JsonProposalPipeline.MergeInto(currentJson, _state.PatchJson, templateJson);
        if (!merge.IsSuccess)
        {
            _state.Error = "应用失败: " + merge.Error;
            return null;
        }

        _state.AppliedCount = merge.ChangedKeys.Count;
        _state.ProposedJson = merge.Json;
        _state.PatchJson = null;
        _state.Error = null;
        return new JsonApplyResult(merge.Json!, merge.ChangedKeys);
    }

    /// <summary>渲染快照：目标不一致时隐藏提示/提案（避免旧提案串到新目标）。</summary>
    public JsonAssistSnapshot Snapshot(string? currentTargetId)
    {
        var same = string.Equals(currentTargetId ?? "", _state.TargetId ?? "", StringComparison.OrdinalIgnoreCase);
        return new JsonAssistSnapshot
        {
            Prompt = same ? _state.Prompt : null,
            ProposedJson = same ? _state.ProposedJson : null,
            Warnings = same ? _state.Warnings : Array.Empty<string>(),
            Error = same ? _state.Error : null,
            AppliedCount = same ? _state.AppliedCount : 0,
            HasPending = same && !string.IsNullOrWhiteSpace(_state.PatchJson),
            IsAvailable = IsAvailable,
        };
    }

    /// <summary>内部状态（同程序集/测试可读；外部请用 <see cref="Snapshot"/>）。</summary>
    internal JsonAssistState State => _state;
}

/// <summary>助理内部状态（唯一持有者）。</summary>
internal sealed class JsonAssistState
{
    public string? TargetId { get; set; }

    public string? Prompt { get; set; }

    public string? PatchJson { get; set; }

    public string? ProposedJson { get; set; }

    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

    public string? Error { get; set; }

    public int AppliedCount { get; set; }
}

/// <summary>应用结果：合并后的 JSON 文本 + 被覆盖键。</summary>
public sealed record JsonApplyResult(string Json, IReadOnlyList<string> ChangedKeys);

/// <summary>助理区域渲染快照（不可变投影；渲染层只读本对象）。</summary>
public sealed class JsonAssistSnapshot
{
    public string? Prompt { get; init; }

    public string? ProposedJson { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public string? Error { get; init; }

    public int AppliedCount { get; init; }

    public bool IsAvailable { get; init; }

    public bool HasPending { get; init; }

    /// <summary>单行状态文本（区域/终端展示用）。</summary>
    public string StatusText
    {
        get
        {
            if (Error is not null) return Error;
            if (!IsAvailable) return "AI 助理未接入（表单仍可手填）";
            if (HasPending) return "已生成提案，待应用（仅覆盖被建议键）";
            if (AppliedCount > 0) return $"已应用 {AppliedCount} 项提案参数";
            if (Warnings.Count > 0) return string.Join("；", Warnings);
            return "等待自然语言描述";
        }
    }
}
