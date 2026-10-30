#nullable enable

namespace UiBackend.Html;

/// <summary>写回目标形态（R2）：具名控件 / 已声明区域 / 页面运行时（目标缺省但计划自描述）/ 不可落地 / 被拒绝的区域目标。</summary>
public enum PageWriteTargetKind
{
    Control = 0,

    /// <summary>已声明区域（<c>region:&lt;key&gt;</c>）。</summary>
    Region,

    /// <summary>
    /// 目标缺省，但产物是自描述写回计划且触发元素归属某页面运行时：
    /// 由该运行时（<see cref="UiBackend.Abstractions.IPageRegionWriter"/>）承接落地。
    /// </summary>
    PageRuntime,

    /// <summary>无可落地目标（纯文本无目标 / 计划无归属页面运行时）。调用方须显式记录，禁静默丢弃。</summary>
    Unlandable,

    /// <summary>区域目标被拒绝（未声明 / 未装配区域目录）。</summary>
    RejectedRegion,
}

/// <summary>
/// 写回目标解析结果（纯结构事实）：<see cref="Target"/> 已归一（去 <c>host:</c> 前缀），
/// <see cref="RegionKey"/> 仅 <see cref="PageWriteTargetKind.Region"/> 时有值；
/// <see cref="Reason"/> 随 <see cref="PageWriteTargetKind.RejectedRegion"/> / <see cref="PageWriteTargetKind.Unlandable"/> 携带（可见化，禁静默丢弃）。
/// </summary>
public readonly record struct PageWriteDisposition(
    string Target,
    PageWriteTargetKind Kind,
    string? RegionKey = null,
    string? Reason = null)
{
    public bool IsRegion => Kind == PageWriteTargetKind.Region;

    public bool IsRejected => Kind == PageWriteTargetKind.RejectedRegion;

    public bool IsPageRuntime => Kind == PageWriteTargetKind.PageRuntime;

    public bool IsUnlandable => Kind == PageWriteTargetKind.Unlandable;

    /// <summary>具名控件目标（空名不算命中）。</summary>
    public bool IsControl => Kind == PageWriteTargetKind.Control && !string.IsNullOrWhiteSpace(Target);
}

/// <summary>
/// 写回路由（R2 单点判定）——**所有写回路径唯一的目标裁决**（事件桥直调 / 交互总线共用），
/// 消除「一处看 binding.output、另一处看计划自描述」的口径分裂（该分裂曾导致点击执行成功但补丁被丢弃）。
/// 判定顺序：
/// 1）声明了 output：<c>region:&lt;key&gt;</c> 须在页面区域目录内，否则拒绝；其余去 <c>host:</c> 前缀按控件处理；
/// 2）未声明 output：产物是自描述写回计划且触发元素有所属页面运行时 → <see cref="PageWriteTargetKind.PageRuntime"/>；
///    否则 <see cref="PageWriteTargetKind.Unlandable"/>（调用方按产物类型决定告警级别）。
/// <para>纯函数（无 IO/无 DOM）：装配校验与运行期写回共用同一判据，避免两处规则漂移。</para>
/// </summary>
public static class PageWriteRouting
{
    public const string HostPrefix = "host:";

    /// <summary>把 <c>host:</c> 前缀归一为裸控件名（其余形态原样返回）。</summary>
    public static string NormalizeTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return string.Empty;
        var trimmed = target.Trim();
        return trimmed.StartsWith(HostPrefix, System.StringComparison.OrdinalIgnoreCase)
            ? trimmed.Substring(HostPrefix.Length)
            : trimmed;
    }

    /// <summary>
    /// 判定写回目标：区域目标 → 查声明目录（未声明即拒绝）；非区域目标 → 去 host 前缀；
    /// 未声明目标时仅当「计划自描述 + 有页面运行时承接」才视为可落地。
    /// <paramref name="regions"/> 缺省（页面未装配区域声明）时，任何 <c>region:</c> 目标均视为未声明。
    /// </summary>
    public static PageWriteDisposition Resolve(
        string? outputTarget,
        RegionCatalog? regions,
        bool hasSelfDescribingPlan = false,
        bool pageRuntimeAvailable = false)
    {
        if (string.IsNullOrWhiteSpace(outputTarget))
        {
            return hasSelfDescribingPlan && pageRuntimeAvailable
                ? new PageWriteDisposition(string.Empty, PageWriteTargetKind.PageRuntime)
                : new PageWriteDisposition(
                    string.Empty,
                    PageWriteTargetKind.Unlandable,
                    Reason: hasSelfDescribingPlan
                        ? "无可承接的页面运行时（触发元素无归属）"
                        : "未声明 output 且产物无自描述写回计划");
        }

        var target = outputTarget.Trim();
        if (!RegionCatalog.IsRegionTarget(target))
            return new PageWriteDisposition(NormalizeTarget(target), PageWriteTargetKind.Control);

        var key = RegionCatalog.Normalize(target);
        if (string.IsNullOrWhiteSpace(key))
            return new PageWriteDisposition(target, PageWriteTargetKind.RejectedRegion, null, "区域键为空");

        if (regions is null)
            return new PageWriteDisposition(target, PageWriteTargetKind.RejectedRegion, key, "页面未声明 regions（区域写回不可用）");

        if (!regions.IsDeclared(key))
            return new PageWriteDisposition(target, PageWriteTargetKind.RejectedRegion, key, $"区域未声明: region:{key}");

        return new PageWriteDisposition(target, PageWriteTargetKind.Region, key);
    }
}
