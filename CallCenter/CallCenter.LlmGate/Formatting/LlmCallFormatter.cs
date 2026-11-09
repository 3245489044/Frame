using System.Globalization;
using System.Text;
using CallCenter.LlmGate.Model;

namespace CallCenter.LlmGate.Formatting;

/// <summary>窗口文本格式化唯一实现（列表首列以 #callId 开头，供参数绑定回解析）。</summary>
public static class LlmCallFormatter
{
    /// <summary>UnknownTokens.Unknown（单源占位词）。</summary>
    public const string Unknown = "未知";
    public const string MissingAttribution = "归属缺失";

    /// <summary>从选中文本解析 callId（`#id ...` 或裸 id；空 ⇒ null）。</summary>
    public static string? ParseCallId(string? selectedText)
    {
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return null;
        }

        var text = selectedText.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        var end = text.IndexOfAny(new[] { ' ', '\t', '|', '\r', '\n' });
        if (end >= 0)
        {
            text = text[..end];
        }

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public static string FormatCallLine(LlmCallSpan span)
    {
        var time = span.StartedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var cost = span.CostAmount.HasValue ? span.CostAmount.Value.ToString("0.######", CultureInfo.InvariantCulture) : Unknown;
        var decision = span.Decision switch
        {
            GateDecision.Allow => "允许",
            GateDecision.Deny => "拒绝",
            _ => "挂起",
        };

        return $"#{span.CallId} {time} | {Display(span.ClientId)} | {Attribution(span.SessionId, span.TaskKey)}"
            + $" | {Display(span.ModelRequest ?? span.ModelResponse)} | {span.StatusCode} | {span.DurationMs}ms"
            + $" | {Tokens(span)} | {cost} | {decision} | {Display(span.ReasonCode ?? span.RuleId)}";
    }

    public static string FormatPendingLine(PendingView pending)
    {
        var time = pending.StartedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var cost = pending.EstimatedCostUsd.HasValue
            ? $"≈{pending.EstimatedCostUsd.Value.ToString("0.######", CultureInfo.InvariantCulture)}"
            : Unknown;
        var fp = string.IsNullOrEmpty(pending.Fingerprint) ? Unknown : pending.Fingerprint[..Math.Min(8, pending.Fingerprint.Length)];

        return $"#{pending.CallId} {time} | {Display(pending.ClientId)} | {Attribution(pending.SessionId, pending.TaskKey)}"
            + $" | {Display(pending.Model)} | {cost} | {Display(pending.RuleId)} | fp:{fp}";
    }

    public static string FormatDetail(LlmCallSpan? span)
    {
        if (span is null)
        {
            return "未选择调用";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[调用] #{span.CallId}  {span.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine($"[归属] client={Display(span.ClientId)} session={Display(span.SessionId)} task={Display(span.TaskKey)} round={Display(span.RoundId)}");
        sb.AppendLine($"[请求] {span.Endpoint}  model={Display(span.ModelRequest)} stream={span.Stream} hop={span.Hop}");
        sb.AppendLine($"        messages={span.MessageCount} roles={Display(span.RoleHistogram)} chars(system/user/history)={span.SystemChars}/{span.UserChars}/{span.HistoryChars}");
        sb.AppendLine($"        tools={span.ToolCount} toolNamesHash={Display(span.ToolNamesHash)} bytes={span.RequestBytes}");
        sb.AppendLine($"[响应] status={span.StatusCode} model={Display(span.ModelResponse)} finish={Display(span.FinishReason)} chars={span.ContentChars} toolCalls={span.ToolCallCount}");
        if (!string.IsNullOrEmpty(span.ErrorType) || !string.IsNullOrEmpty(span.ErrorCode))
        {
            sb.AppendLine($"        error={Display(span.ErrorType)}/{Display(span.ErrorCode)}");
        }

        sb.AppendLine($"[用量] in={TokenText(span.TokenIn)} out={TokenText(span.TokenOut)} cacheRead={TokenText(span.CacheRead)} cacheWrite={TokenText(span.CacheWrite)}");
        var cost = span.CostAmount.HasValue ? span.CostAmount.Value.ToString("0.########", CultureInfo.InvariantCulture) : Unknown;
        sb.AppendLine($"[成本] amount={cost} {span.CostCurrency} source={span.CostSource}");
        foreach (var item in span.CostBreakdown)
        {
            sb.AppendLine($"        - {item.Item}={item.Amount.ToString("0.########", CultureInfo.InvariantCulture)}{(item.Basis is null ? string.Empty : $"（{item.Basis}）")}");
        }
        var decidedBy = span.DecidedBy switch
        {
            DecidedBy.Rule => "rule",
            DecidedBy.Manual => "manual",
            DecidedBy.Timeout => "timeout",
            _ => "bypass",
        };
        sb.AppendLine($"[裁决] {span.Decision} rule={Display(span.RuleId)} reason={Display(span.ReasonCode)} by={decidedBy} at={span.DecidedAt?.ToLocalTime():HH:mm:ss.fff}");
        sb.AppendLine($"[指纹] {span.RequestFingerprint}");
        sb.AppendLine($"[时间线] start={span.StartedAt.ToLocalTime():HH:mm:ss.fff} end={span.EndedAt?.ToLocalTime():HH:mm:ss.fff} duration={span.DurationMs}ms");
        if (span.RequestBodySampled)
        {
            sb.AppendLine($"[请求预览] {Display(span.RequestPreview)}");
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatStats(GateStats stats)
    {
        var tokens = stats.Tokens.HasValue ? stats.Tokens.Value.ToString("N0", CultureInfo.InvariantCulture) : Unknown;
        var costs = stats.Costs.HasValue ? stats.Costs.Value.ToString("0.######", CultureInfo.InvariantCulture) : Unknown;
        var top = stats.TopFingerprint is null
            ? "-"
            : $"{stats.TopFingerprint[..Math.Min(8, stats.TopFingerprint.Length)]}({stats.TopFingerprintCount})";

        return $"今日：调用 {stats.Calls} / 成功 {stats.Success} / 失败 {stats.Failure} / 拒绝 {stats.Denied}"
            + $" / 待审批 {stats.Pending} / token {tokens} / 成本 {costs} / 未知 {stats.UnknownCosts}"
            + $" / 丢弃 {stats.Dropped} / Top1 {top}";
    }

    private static string Tokens(LlmCallSpan span)
        => span.TokenIn.HasValue || span.TokenOut.HasValue
            ? $"{TokenText(span.TokenIn)}/{TokenText(span.TokenOut)}"
            : Unknown;

    private static string TokenText(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? Unknown;

    private static string Attribution(string? session, string? task)
        => string.IsNullOrWhiteSpace(session) && string.IsNullOrWhiteSpace(task)
            ? MissingAttribution
            : $"{Display(session)}/{Display(task)}";

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? Unknown : value!;
}
