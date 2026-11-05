using System;
using System.Collections.Generic;
using System.Linq;
using Modules.PathBoundary;

namespace Share.ObjectiveBoundary;

/// <summary>
/// 空间边界评估器（纯函数，无副作用）。校验目标工作根目录是否在 AllowedRoots 白名单内、
/// 未命中 DenyPaths、深度未超限；越界时按 ExpandPolicy 决定拒绝（回退安全根）或扩界。
/// 输出 BoundaryAssessment 的空间维度（EffectiveRoot / SpatialAllowed / SpatialDenyReason）。
/// 根包含与拒绝匹配统一走 <see cref="PathBoundary"/> 的路径段模型（ObjectiveBoundary.md §4.12）。
/// </summary>
public static class SpatialBoundaryEvaluator
{
    public static BoundaryAssessment Resolve(SpatialBoundaryConfig config, string? requestedRoot)
        => Resolve(config, requestedRoot, prior: null);

    /// <summary>
    /// 带上一轮状态的解析：显式请求根缺失时用 <paramref name="prior"/>.<c>EffectiveRoot</c> 作为请求根，
    /// 打通"扩界后有效根可在后续轮继承"的闭环（§4.13：原实现无任何请求根写入者，expand 分支不可达）。
    /// </summary>
    public static BoundaryAssessment Resolve(
        SpatialBoundaryConfig config, string? requestedRoot, BoundaryContext? prior)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        // 过滤无效白名单项，避免 Path.GetFullPath(null/空) 崩溃（C3 防御）。
        var allowed = ResolveAllowedRoots(config)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(PathBoundary.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (allowed.Count == 0)
            throw new InvalidOperationException(
                "空间边界配置缺少有效允许根目录（AllowedRoots/BaseRoot 未配置）。");

        var safeRoot = allowed[0];

        // 请求根优先级：显式请求 → 上一轮有效根 → 安全根（不再回退进程 CWD，§4.11：CWD 不可复现）。
        var target = !string.IsNullOrWhiteSpace(requestedRoot)
            ? PathBoundary.Normalize(requestedRoot)
            : !string.IsNullOrWhiteSpace(prior?.EffectiveRoot)
                ? PathBoundary.Normalize(prior!.EffectiveRoot!)
                : safeRoot;

        var withinAllowed = allowed.Any(root => PathBoundary.IsWithin(root, target));
        var denyHit = PathBoundary.IsDenied(target, config.DenyPaths ?? new List<string>());

        // 越界判定：不在允许根内，或命中禁用路径
        bool allowedPath = withinAllowed && !denyHit;

        if (allowedPath)
            return new BoundaryAssessment(false, 0d, 0d, 1d, 0d,
                BoundaryActionKind.None, null, target, true, null, 0d);

        if (config.ExpandPolicy?.Equals("expand", StringComparison.OrdinalIgnoreCase) == true && !denyHit)
            return new BoundaryAssessment(false, 0d, 0d, 1d, 0d,
                BoundaryActionKind.ExpandRoot, $"扩界至 {target}", target, true, null, 0d);

        var reason = denyHit
            ? $"目标路径命中禁用路径：{target}"
            : $"目标路径不在允许根内：{target}（allowed: {string.Join(", ", allowed)}）";
        return new BoundaryAssessment(false, 0d, 0d, 1d, 0d,
            BoundaryActionKind.None, $"空间越界，回退安全根 {safeRoot}", safeRoot, false, reason, 0d);
    }

    /// <summary>
    /// 允许根来源：显式白名单 → BaseRoot；两者都缺失时返回空，由调用方抛出配置错误。
    /// 原实现回退 <see cref="Directory.GetCurrentDirectory"/>，使"缺少配置"变成不可复现的隐式放行（§4.11）。
    /// </summary>
    private static List<string> ResolveAllowedRoots(SpatialBoundaryConfig config)
    {
        if (config.AllowedRoots is { Count: > 0 }) return config.AllowedRoots;
        if (!string.IsNullOrWhiteSpace(config.BaseRoot)) return new List<string> { config.BaseRoot };
        return new List<string>();
    }
}
