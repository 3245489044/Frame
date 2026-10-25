#nullable enable

namespace UiBackend.Html;

/// <summary>写回目标形态（R2）：普通控件 / 已声明区域 / 被拒绝的区域目标。</summary>
public enum PageWriteTargetKind
{
    Control = 0,
    Region,
    RejectedRegion,
}

/// <summary>
/// 写回目标解析结果（纯结构事实）：<see cref="Target"/> 已归一（去 <c>host:</c> 前缀），
/// <see cref="RegionKey"/> 仅 <see cref="PageWriteTargetKind.Region"/> 时有值；
/// <see cref="PageWriteTargetKind.RejectedRegion"/> 携带 <see cref="Reason"/>（装配/运行期可见化，禁静默丢弃）。
/// </summary>
public readonly record struct PageWriteDisposition(
    string Target,
    PageWriteTargetKind Kind,
    string? RegionKey = null,
    string? Reason = null)
{
    public bool IsRegion => Kind == PageWriteTargetKind.Region;

    public bool IsRejected => Kind == PageWriteTargetKind.RejectedRegion;
}

/// <summary>
/// 写回路由（R2 单点判定）：<c>region:&lt;key&gt;</c> 仅当声明面存在该区域才合法；
/// <c>host:</c> 前缀去前缀后按控件名解析（消除前缀造成的「目标非 HtmlViewer」误判）。
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
    /// 判定写回目标：区域目标 → 查声明目录（未声明即拒绝）；非区域目标 → 去 host 前缀。
    /// <paramref name="regions"/> 缺省（页面未装配区域声明）时，任何 <c>region:</c> 目标均视为未声明。
    /// </summary>
    public static PageWriteDisposition Resolve(string? outputTarget, RegionCatalog? regions)
    {
        if (string.IsNullOrWhiteSpace(outputTarget))
            return new PageWriteDisposition(string.Empty, PageWriteTargetKind.Control);

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