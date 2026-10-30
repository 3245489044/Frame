using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FrontEndDriven.ProductionConsole.Assistant;

/// <summary>
/// AI 参数助理（表单形态适配器）：JSON 单步内核 → 声明键裁剪/归一 → 待应用；
/// 应用时只覆盖被建议键。UI 渲染与命令路由只做薄委托，不含业务。
/// </summary>
public sealed class ParameterAssistant
{
    private readonly ParameterAssistantState _state;
    private readonly IJsonProposalSource? _source;

    public ParameterAssistant(ParameterAssistantState? state = null, IJsonProposalSource? source = null)
    {
        _state = state ?? new ParameterAssistantState();
        _source = source;
    }

    public ParameterAssistantState State => _state;

    /// <summary>能力切换/表单重置 → 清空提案态（唯一失效点）。</summary>
    public void Invalidate(string? capabilityId)
    {
        _state.CapabilityId = capabilityId;
        _state.Prompt = null;
        _state.PendingProposal = null;
        _state.ProposalError = null;
        _state.AppliedCount = 0;
    }

    /// <summary>
    /// 生成提案：来源返回的 JSON 经「只保留能力声明键 + 按控件形态归一」裁剪后写入 pending；
    /// 未接入/超时/解析失败/校验失败一律折叠为可见错误，不伪装成功。
    /// </summary>
    public async Task<bool> ProposeAsync(
        CapabilityDescriptor capability, string? text, CancellationToken ct = default)
    {
        _state.CapabilityId = capability.Id;
        _state.AppliedCount = 0;
        _state.PendingProposal = null;
        _state.ProposalError = null;

        var trimmed = text?.Trim();
        _state.Prompt = trimmed;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            _state.ProposalError = "请输入一句自然语言描述再解析";
            return false;
        }

        if (_source is null)
        {
            _state.ProposalError = "AI 参数助理未接入（插件未提供提案来源）";
            return false;
        }

        JsonProposal? proposal;
        try
        {
            proposal = await _source.ProposeAsync(new JsonProposalRequest
            {
                TargetId = capability.Id,
                Prompt = trimmed,
                SchemaText = JsonSchemaText.FromSpecs(capability.Params),
                Specs = capability.Params,
                CurrentJson = null,
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _state.ProposalError = $"AI 解析异常: {ex.Message}";
            return false;
        }

        var pending = proposal is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : JsonProposalPipeline.ToSpecValues(proposal.Json, capability.Params);
        if (pending.Count == 0)
        {
            _state.ProposalError = "AI 未返回可用参数（可能超时或解析失败），请重试或手动填写";
            return false;
        }

        _state.PendingProposal = pending;
        var errors = ParamSet.Validate(capability.Params, ParamSet.Merge(capability.Params, pending));
        if (errors.Count > 0) _state.ProposalError = "提案校验: " + string.Join("；", errors);
        return true;
    }

    /// <summary>应用 pending：只覆盖被建议键，其余表单值保持不变；能力不一致或无 pending → 0。</summary>
    public int ApplyTo(string? capabilityId, IDictionary<string, object?> formValues)
    {
        if (!string.Equals(capabilityId, _state.CapabilityId, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (_state.PendingProposal is not { Count: > 0 } pending) return 0;

        var applied = 0;
        foreach (var kv in pending)
        {
            formValues[kv.Key] = kv.Value;
            applied++;
        }

        _state.AppliedCount = applied;
        _state.PendingProposal = null;
        _state.ProposalError = null;
        return applied;
    }

    /// <summary>渲染快照：能力不一致时隐藏提示/提案（避免旧提案串到新能力）。</summary>
    public ParameterAssistantView Snapshot(string? currentCapabilityId)
    {
        var same = string.Equals(currentCapabilityId, _state.CapabilityId, StringComparison.OrdinalIgnoreCase);
        return new ParameterAssistantView
        {
            Prompt = same ? _state.Prompt : null,
            PendingProposal = same ? _state.PendingProposal : null,
            ProposalError = same ? _state.ProposalError : null,
            IsAvailable = _source is not null,
        };
    }
}
