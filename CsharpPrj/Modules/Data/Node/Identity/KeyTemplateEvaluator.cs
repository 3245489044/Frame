using Modules.Node.Placement;
using Modules.Parsing.Keys;

namespace Modules.Node.Identity;

/// <summary>
/// 键模板求值（**转发门面**）：正向求值（值 → 键）与反向解析（键 → 值）。
/// <para>
/// 键面收敛（2026-09-26）：唯一实现已下沉 <c>Modules.Parsing.Keys</c>（BCL-only 键文法内核，
/// <see cref="KeyTemplates"/>）；本类仅保留域侧入口 —— 位置注册表联合求值（未装配 / 未声明 / 缺值 ⇒ reason 可见化），
/// 以及既有公开签名（零消费方改动，P1 迁移纪律）。本工程禁再写占位符正则或手写切片。
/// </para>
/// <para>位置键 = 键模板(身份键)（N2：位置不改变身份），故读写两侧共用同一内核求值器。</para>
/// </summary>
public static class KeyTemplateEvaluator
{
    /// <summary>占位符名（序 = 模板序）。</summary>
    public static IReadOnlyList<string> Placeholders(string template) =>
        KeyTemplates.Placeholders(template);

    /// <summary>正向求值（缺值 ⇒ false + <paramref name="missing"/>；不产出半成品键）。</summary>
    public static bool TryEvaluate(
        string template,
        IReadOnlyDictionary<string, string> values,
        out string key,
        out IReadOnlyList<string> missing) =>
        KeyTemplates.TryEvaluate(template, values, out key, out missing);

    /// <summary>正向求值（缺值即抛；调用方须先经 <see cref="TryEvaluate"/> 可见化）。</summary>
    public static string Evaluate(string template, IReadOnlyDictionary<string, string> values) =>
        KeyTemplates.Evaluate(template, values);

    /// <summary>锚定反解正则（诊断 / 机检用）。</summary>
    public static string ToRegexPattern(string template) => KeyTemplates.ToRegexPattern(template);

    /// <summary>反向反解（与该模板不同形 ⇒ false + 空值袋，不猜）。</summary>
    public static bool TryParseValues(
        string template,
        string key,
        out IReadOnlyDictionary<string, string> values) =>
        KeyTemplates.TryParseValues(template, key, out values);

    /// <summary>L-4：模板是否含声明的序数占位（缺失即序数无法单源分配）。</summary>
    public static bool ContainsSequencePlaceholder(
        string template,
        IReadOnlyCollection<string> sequenceKeys) =>
        sequenceKeys.Any(key => KeyTemplates.ContainsPlaceholder(template, key));

    /// <summary>
    /// 同一域内键模板可区分性判定（装配期校验用）：按<b>形状</b>（占位符匿名化）比较 ——
    /// <c>{a}</c> 与 <c>{b}</c>、<c>{a}.{b}</c> 与 <c>{b}.{a}</c> 键空间等价，必须判为不可区分
    /// （原实现按命名正则文本比较，组名不同即误判为"可区分"，该门近乎恒真）。
    /// </summary>
    public static bool TemplatesDistinguishable(string left, string right) =>
        KeyTemplates.IsDistinguishable(left, right);

    /// <summary>
    /// 位置键求值（**域侧唯一入口**，F2）：位置声明 → 键模板 → 键。
    /// <para>
    /// 未装配（位置注册表缺失 / 位置未声明 / 取值不全）⇒ <c>false</c> + reason，由调用方**可见化**；
    /// 禁回退常量（N9 / D4①：未装配不兜底）。
    /// </para>
    /// </summary>
    /// <param name="placements">位置注册表（声明装载产物；<c>null</c> = 未装配）。</param>
    /// <param name="placementId">位置 id（声明面）。</param>
    /// <param name="values">身份占位符 → 值。</param>
    /// <param name="key">位置键（成功时给出）。</param>
    /// <param name="reason">失败原因（未装配 / 未声明 / 缺值；逐字可诊断）。</param>
    public static bool TryEvaluatePlacement(
        PlacementRegistry? placements,
        string placementId,
        IReadOnlyDictionary<string, string> values,
        out string key,
        out string reason)
    {
        key = string.Empty;

        if (placements is null)
        {
            reason = "placement.unbound（位置注册表未装配：键构造点不得回退常量）";
            return false;
        }

        if (string.IsNullOrWhiteSpace(placementId))
        {
            reason = "placement.id-empty";
            return false;
        }

        if (!placements.TryGet(placementId, out var spec))
        {
            reason = $"placement.undeclared（位置 '{placementId}' 未声明；位置清单单源 = 域声明）";
            return false;
        }

        if (!TryEvaluate(spec.KeyTemplate, values, out key, out var missing))
        {
            reason = $"placement.value-missing（模板 '{spec.KeyTemplate}' 缺值: {string.Join(",", missing)}）";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>位置键求值（框架入口；语义同 <see cref="TryEvaluatePlacement(PlacementRegistry?, string, IReadOnlyDictionary{string, string}, out string, out string)"/>）。</summary>
    public static bool TryEvaluatePlacement(
        NodeFramework? framework,
        string placementId,
        IReadOnlyDictionary<string, string> values,
        out string key,
        out string reason)
        => TryEvaluatePlacement(framework?.Placements, placementId, values, out key, out reason);

    /// <summary>
    /// 位置键反解（**域侧唯一入口**（F2 反向））：位置键 → 身份占位符值。
    /// 未装配 / 位置未声明 / 键与该位置模板不同形 ⇒ <c>false</c> + reason（调用方可见化，禁回退常量）。
    /// </summary>
    /// <param name="placements">位置注册表（声明装载产物；<c>null</c> = 未装配）。</param>
    /// <param name="placementId">位置 id（声明面）。</param>
    /// <param name="key">位置键。</param>
    /// <param name="values">反解值（失败时空字典）。</param>
    /// <param name="reason">失败原因。</param>
    public static bool TryParsePlacementValues(
        PlacementRegistry? placements,
        string placementId,
        string key,
        out IReadOnlyDictionary<string, string> values,
        out string reason)
    {
        values = EmptyValues;

        if (placements is null)
        {
            reason = "placement.unbound（位置注册表未装配：键反解点不得回退常量）";
            return false;
        }

        if (string.IsNullOrWhiteSpace(placementId))
        {
            reason = "placement.id-empty";
            return false;
        }

        if (!placements.TryGet(placementId, out var spec))
        {
            reason = $"placement.undeclared（位置 '{placementId}' 未声明；位置清单单源 = 域声明）";
            return false;
        }

        if (!TryParseValues(spec.KeyTemplate, key, out var parsed))
        {
            values = EmptyValues;
            reason = $"placement.key-unparsable（键 '{key}' 与位置 '{placementId}' 的模板 '{spec.KeyTemplate}' 不同形）";
            return false;
        }

        values = parsed;
        reason = string.Empty;
        return true;
    }

    /// <summary>位置键反解（框架入口；语义同 <see cref="TryParsePlacementValues(PlacementRegistry?, string, string, out IReadOnlyDictionary{string, string}, out string)"/>）。</summary>
    public static bool TryParsePlacementValues(
        NodeFramework? framework,
        string placementId,
        string key,
        out IReadOnlyDictionary<string, string> values,
        out string reason)
        => TryParsePlacementValues(framework?.Placements, placementId, key, out values, out reason);

    /// <summary>反解失败/未装配时的空值集（调用方无需判 null）。</summary>
    private static readonly IReadOnlyDictionary<string, string> EmptyValues =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
