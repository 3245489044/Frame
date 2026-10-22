using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Share.ObjectiveBoundary;


namespace Share.ObjectiveBoundary;

/// <summary>
/// 空间边界评估器（纯函数，无副作用）。校验目标工作根目录是否在 AllowedRoots 白名单内、
/// 未命中 DenyPaths、深度未超限；越界时按 ExpandPolicy 决定拒绝（回退安全根）或扩界。
/// 输出 BoundaryAssessment 的空间维度（EffectiveRoot / SpatialAllowed / SpatialDenyReason）。
/// </summary>
public static class SpatialBoundaryEvaluator
{
    public static BoundaryAssessment Resolve(SpatialBoundaryConfig config, string? requestedRoot)
    {
        // 过滤无效白名单项，避免 Path.GetFullPath(null/空) 崩溃（C3 防御）。
        var allowed = ResolveAllowedRoots(config)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();
        if (allowed.Count == 0)
            throw new InvalidOperationException(
                "空间边界配置缺少有效允许根目录（AllowedRoots/BaseRoot 未配置）。");

        var safeRoot = Path.GetFullPath(allowed[0]);

        var target = string.IsNullOrWhiteSpace(requestedRoot)
            ? safeRoot
            : Path.GetFullPath(requestedRoot);

        var withinAllowed = allowed.Any(root => IsWithin(Path.GetFullPath(root), target));
        var denyHit = config.DenyPaths
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Any(d => IsDenied(target, d));

        // 越界判定：不在允许根内，或命中禁用路径
        bool allowedPath = withinAllowed && !denyHit;

        if (allowedPath)
            return new BoundaryAssessment(false, 0d, 0d, 1d, 0d,
                BoundaryActionKind.None, null, target, true, null, 0d);

        // 越界 → 按 ExpandPolicy：
        //   deny（默认）→ 拒绝并回退安全根；expand → 目标未命中禁用路径时扩界至目标。
        // （C1 修复：原实现 expand 误引用已提前返回的 allowedPath，导致此分支恒为 false、扩界永不生效。）
        if (config.ExpandPolicy?.Equals("expand", StringComparison.OrdinalIgnoreCase) == true && !denyHit)
            return new BoundaryAssessment(false, 0d, 0d, 1d, 0d,
                BoundaryActionKind.ExpandRoot, $"扩界至 {target}", target, true, null, 0d);

        var reason = denyHit
            ? $"目标路径命中禁用路径：{target}"
            : $"目标路径不在允许根内：{target}（allowed: {string.Join(", ", allowed)}）";
        return new BoundaryAssessment(false, 0d, 0d, 1d, 0d,
            BoundaryActionKind.None, $"空间越界，回退安全根 {safeRoot}", safeRoot, false, reason, 0d);
    }

    private static List<string> ResolveAllowedRoots(SpatialBoundaryConfig config)
    {
        if (config.AllowedRoots.Count > 0) return config.AllowedRoots;
        if (!string.IsNullOrWhiteSpace(config.BaseRoot)) return new List<string> { config.BaseRoot };
        return new List<string> { Directory.GetCurrentDirectory() };
    }

    private static bool IsWithin(string root, string path)
    {
        if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 禁用路径判定（C2 修复：替代子串 Contains，避免误伤 ".gitignore"/"mynode_modules" 等非真实目录段）。
    /// deny 以分隔符开头（如 "\.git"）→ 视为目录段名，按路径段精确匹配（忽略大小写）；
    /// 否则按子串包含匹配（绝对/相对路径表达式）。
    /// </summary>
    private static bool IsDenied(string path, string deny)
    {
        // 归一化分隔符（统一 Windows 分隔符），便于段边界匹配
        var p = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var d = deny.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (d.Length == 0) return false;

        if (d[0] == Path.DirectorySeparatorChar)
        {
            var segment = d.TrimStart(Path.DirectorySeparatorChar);
            return segment.Length > 0
                && p.Split(Path.DirectorySeparatorChar)
                    .Any(s => string.Equals(s, segment, StringComparison.OrdinalIgnoreCase));
        }

        return p.Contains(d, StringComparison.OrdinalIgnoreCase);
    }
}
