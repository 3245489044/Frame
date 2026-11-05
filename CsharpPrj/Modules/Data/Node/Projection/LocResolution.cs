using Modules.Node.Placement;

namespace Modules.Node.Projection;

/// <summary>
/// loc（位置轴坐标）**声明派生单源** —— 读侧位置来源由域声明 <c>[Space] Loc</c> 反解，
/// 替代散落硬编码常量（控制反转：loc 由声明给出，而非调用点常量）。
/// <para>
/// 纪律：loc 必须**声明派生**（声明级判据 CT-L6：<c>Resolve(loc)</c> 位置集 ≡ 声明位置集）；
/// 声明缺失 / 反解不命中 ⇒ <c>null</c>（不兜底常量，读投影按注册表直取）。
/// </para>
/// </summary>
public static class LocResolution
{
    /// <summary>按域声明反解 loc 值（未命中 ⇒ <c>null</c>，不伪造）。</summary>
    /// <param name="framework">该域框架实例（声明装载产物）。</param>
    /// <param name="placementIds">目标位置 id 集（loc 须至少覆盖其中之一）。</param>
    public static string? Derive(NodeFramework? framework, IReadOnlyList<string>? placementIds)
    {
        if (framework is null || placementIds is not { Count: > 0 } ||
            framework.PlacementResolver is not DeclaredPlacementResolver declared)
        {
            return null;
        }

        foreach (var loc in declared.DeclaredLocs)
        {
            var resolution = declared.Resolve(loc, string.Empty);
            if (resolution.Resolved &&
                resolution.Placements.Any(p => placementIds.Contains(p.PlacementId, StringComparer.Ordinal)))
            {
                return loc;
            }
        }

        return null;
    }
}
