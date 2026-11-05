namespace Share.ContextGeometry;

/// <summary>委托模式（声明驱动；禁运行时 LLM 判断）。</summary>
public enum DelegationMode
{
    /// <summary>全部可委托。</summary>
    Always,

    /// <summary>全部内联（不产生委托）。</summary>
    Never,
}

/// <summary>一次工具调用意图（委托判据输入；结构事实）。</summary>
public readonly record struct ToolIntent(
    string ToolName,
    string ArgumentsJson = "",
    int EstimatedTokens = 0,
    string? AgentTag = null);

/// <summary>单条委托计划（委托 or 内联 + 诊断码）。</summary>
public readonly record struct DelegationItem(ToolIntent Intent, bool Delegated, string Code);

/// <summary>
/// 委托计划器（纯函数）：模式与排除清单 ⇒ 委托/内联。
/// <para>判据唯一 = <see cref="DelegationPolicy.ShouldDelegate"/>；本类只做批量编排。</para>
/// </summary>
public static class DelegationPlanner
{
    /// <summary>解析模式（未知取值 ⇒ <see cref="DelegationMode.Always"/>，不静默改语义）。</summary>
    public static DelegationMode ParseMode(string? mode)
        => string.Equals(mode?.Trim(), "never", StringComparison.OrdinalIgnoreCase)
            ? DelegationMode.Never
            : DelegationMode.Always;

    /// <summary>批量计划（never ⇒ 全内联；排除命中 ⇒ 内联；空工具名 ⇒ 内联 + 诊断）。</summary>
    /// <remarks>
    /// 未识别的 mode 取值不再静默按 <see cref="DelegationMode.Always"/> 处理：
    /// 行为保持 fail-safe（仍按 Always 委托），但每条结果显式标注 <see cref="UnknownModeCode"/>，
    /// 使「拼错的声明」在出口可见（对齐 AX.NoSilentOutlet）。
    /// </remarks>
    public static IReadOnlyList<DelegationItem> Plan(
        IEnumerable<ToolIntent>? intents,
        string? mode,
        IReadOnlyCollection<string>? excludeTools)
    {
        var parsed = ParseMode(mode);
        var unknownMode = IsUnknownMode(mode);
        var items = new List<DelegationItem>();
        foreach (var intent in intents ?? Enumerable.Empty<ToolIntent>())
        {
            if (parsed == DelegationMode.Never)
            {
                items.Add(new DelegationItem(intent, false, "delegate_mode_never"));
                continue;
            }

            if (unknownMode)
            {
                items.Add(new DelegationItem(intent, true, UnknownModeCode));
                continue;
            }

            if (string.IsNullOrWhiteSpace(intent.ToolName))
            {
                items.Add(new DelegationItem(intent, false, "intent_tool_missing"));
                continue;
            }

            var delegated = DelegationPolicy.ShouldDelegate(
                intent.ToolName, intent.EstimatedTokens, excludeTools);
            items.Add(new DelegationItem(
                intent, delegated, delegated ? "delegate" : GeometryDiagnostics.DelegateExcluded));
        }
        return items;
    }

    /// <summary>未识别模式声明的诊断码。</summary>
    public const string UnknownModeCode = "delegate_mode_unknown";

    /// <summary>模式取值是否不可识别（空/缺省不算：空 = 采用缺省 Always）。</summary>
    public static bool IsUnknownMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return false;
        var value = mode.Trim();
        return !string.Equals(value, "always", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(value, "never", StringComparison.OrdinalIgnoreCase);
    }
}
