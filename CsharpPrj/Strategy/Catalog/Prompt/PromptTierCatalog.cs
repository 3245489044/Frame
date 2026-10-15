using System;
using System.Collections.Generic;
using System.Text;
using Strategy.Catalog.Core;

namespace Strategy.Catalog.Prompt;

/// <summary>
/// 提示词层分级注册表（PromptTierCatalog）—— 与 <c>PromptSegmentCatalog</c> 同构的**代码单一声明**：
/// 一行 <see cref="Register"/> 接入一个提示词级目录面；新增分级组 = 加一行，不碰别处。
/// <para>
/// 机制与语义分离（红线）：框架内零领域字符串；<c>SourceId</c>/<c>AxisId</c>/<c>Key</c> 等语义值
/// 由上层（组合根）经 <see cref="Register"/> 注入。
/// </para>
/// <para>完备性判据见 <see cref="Validate"/>（可机械复算：字段齐备 + BandOrder 非降）。</para>
/// </summary>
public static class PromptTierCatalog
{
    private static readonly object Gate = new();
    private static readonly List<TierSpec> Rows = new();

    /// <summary>已声明分级组（声明序 = 默认输出序）。</summary>
    public static IReadOnlyList<TierSpec> Declared
    {
        get { lock (Gate) return Rows.ToArray(); }
    }

    /// <summary>声明一个分级组（幂等：同 Key 重复忽略，保持显式装配可复现）。</summary>
    public static void Register(TierSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (string.IsNullOrWhiteSpace(spec.Key))
            throw new ArgumentException("分级组 Key 不可为空。", nameof(spec));

        lock (Gate)
        {
            foreach (var existing in Rows)
                if (string.Equals(existing.Key, spec.Key, StringComparison.Ordinal)) return;
            Rows.Add(spec);
        }
    }

    /// <summary>批量声明（序 = 传入序）。</summary>
    public static void RegisterAll(IEnumerable<TierSpec>? specs)
    {
        foreach (var spec in specs ?? Array.Empty<TierSpec>())
        {
            if (spec is null) continue;
            Register(spec);
        }
    }

    /// <summary>清空（测试隔离：使装配态可复现）。</summary>
    public static void Clear()
    {
        lock (Gate) Rows.Clear();
    }

    /// <summary>
    /// 完备性判据（可机械复算）：每分级组须同时具备 Key/UiStage/SourceId/AxisId/Project，且 BandOrder 序列**非降**。
    /// </summary>
    public static IReadOnlyList<string> Validate()
    {
        var violations = new List<string>();
        var prev = int.MinValue;
        foreach (var spec in Declared)
        {
            if (string.IsNullOrWhiteSpace(spec.Key)) violations.Add("分级组缺 Key");
            if (string.IsNullOrWhiteSpace(spec.UiStage)) violations.Add($"分级组 {spec.Key} 缺 UiStage");
            if (string.IsNullOrWhiteSpace(spec.SourceId)) violations.Add($"分级组 {spec.Key} 缺 SourceId");
            if (string.IsNullOrWhiteSpace(spec.AxisId)) violations.Add($"分级组 {spec.Key} 缺 AxisId");
            if (spec.Project is null) violations.Add($"分级组 {spec.Key} 缺 Project（投影函数）");
            if (spec.BandOrder < prev)
                violations.Add($"分级组 {spec.Key} 的 BandOrder({spec.BandOrder}) 低于前组({prev})：顺序须按 BandOrder 升序");
            prev = spec.BandOrder;
        }
        return violations;
    }

    /// <summary>
    /// 投影全部已声明分级组为段文本（索引档）。未注册源 / 投影异常 → 跳过该行（不阻断，零回归）。
    /// </summary>
    public static string ProjectAll(CatalogQueryService queryService)
    {
        ArgumentNullException.ThrowIfNull(queryService);
        var sb = new StringBuilder();
        foreach (var spec in Declared)
        {
            string text;
            try { text = spec.Project(queryService) ?? string.Empty; }
            catch (Exception) { continue; }
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(text);
        }
        return sb.ToString();
    }
}
