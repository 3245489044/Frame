using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Modules.PathBoundary;

/// <summary>
/// 路径边界唯一内核：把"允许根包含"与"拒绝路径"统一到同一 <b>路径段序列</b> 模型。
/// 替代原 ObjectiveBoundary 内两套矛盾模型（允许根用字符串前缀、拒绝用目录段/substring）。
/// 纯函数、BCL-only；调用方负责把相对路径归一为绝对路径。
/// </summary>
public static class PathBoundary
{
    private static readonly char Separator = System.IO.Path.DirectorySeparatorChar;
    private static readonly StringComparison SegmentComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>归一化：绝对路径 + 统一为平台主分隔符。</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("路径不得为空。", nameof(path));

        var full = System.IO.Path.GetFullPath(path);
        return full.Replace(System.IO.Path.AltDirectorySeparatorChar, Separator);
    }

    /// <summary>
    /// 根包含判定（段前缀语义）：<paramref name="path"/> 的起始段序列必须与 <paramref name="root"/> 完全一致，
    /// 从而 "d:\repo" 不包含 "d:\repo-evil"（前缀实现的真实误判）。
    /// </summary>
    public static bool IsWithin(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
            return false;

        var rootSegments = Segments(Split(root));
        var pathSegments = Segments(Split(path));
        if (rootSegments.Length == 0 || rootSegments.Length > pathSegments.Length)
            return false;

        for (var i = 0; i < rootSegments.Length; i++)
        {
            if (!string.Equals(rootSegments[i], pathSegments[i], SegmentComparison))
                return false;
        }
        return true;
    }

    /// <summary>
    /// 拒绝匹配（段窗口语义）：拒绝表达式按路径段切分后，在目标路径的段序列中做连续窗口匹配。
    /// 单段表达式（如 ".git" / "node_modules"）等价"任一段精确相等"，不会误伤 ".gitignore"/"mynode_modules"；
    /// 多段表达式（如 "C:\repo\.git"）按连续段序列匹配。
    /// </summary>
    public static bool IsDenied(string path, string denyExpression)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(denyExpression))
            return false;

        var pathSegments = Segments(Split(path));
        var denySegments = Segments(Split(denyExpression.TrimStart('/', '\\')));
        if (denySegments.Length == 0 || denySegments.Length > pathSegments.Length)
            return false;

        for (var start = 0; start <= pathSegments.Length - denySegments.Length; start++)
        {
            var matched = true;
            for (var i = 0; i < denySegments.Length; i++)
            {
                if (!string.Equals(pathSegments[start + i], denySegments[i], SegmentComparison))
                {
                    matched = false;
                    break;
                }
            }
            if (matched) return true;
        }
        return false;
    }

    /// <summary>任一拒绝表达式命中即拒绝。</summary>
    public static bool IsDenied(string path, IEnumerable<string> denyExpressions)
    {
        if (denyExpressions == null) return false;
        foreach (var deny in denyExpressions)
        {
            if (!string.IsNullOrWhiteSpace(deny) && IsDenied(path, deny)) return true;
        }
        return false;
    }

    private static string Split(string path)
        => path.Replace(System.IO.Path.AltDirectorySeparatorChar, Separator);

    private static string[] Segments(string path)
        => path.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries);
}
