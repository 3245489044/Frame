#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;
using FrontEndDriven.Diagnostics;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 声明字体族归一化（**唯一实现**）：CSS 候选列表 → 本机已安装的第一个 family。
/// <para>
/// WinForms 的 <c>new Font("Segoe UI, Microsoft YaHei, sans-serif", …)</c> 不认候选列表，
/// 会静默回落到 Microsoft Sans Serif 且不抛异常；因此归一化必须发生在写描述符之前，
/// 未命中路径以 <c>FontFallback</c> 可见化（禁静默换字）。
/// </para>
/// <para>候选顺序由声明（<c>ui.ushar</c> 的 <c>font_ui</c>）决定，代码不写优先级。</para>
/// </summary>
public static class SurfaceFontResolver
{
    private static readonly object Gate = new();

    private static HashSet<string>? _installed;

    /// <summary>同一声明值只记一条降级日志（防每控件刷屏）。</summary>
    private static readonly ConcurrentDictionary<string, byte> Logged = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>CSS 通用族名：不是可安装 family，跳过而不算未命中。</summary>
    private static readonly HashSet<string> GenericFamilies =
        new(new[] { "sans-serif", "serif", "monospace", "cursive", "fantasy", "system-ui", "ui-sans-serif", "ui-serif" },
            StringComparer.OrdinalIgnoreCase);

    /// <summary>解析结果：<c>Family</c> 永不为空；<c>FellBack</c> = 声明候选全部不可用。</summary>
    /// <param name="Family">实际可用于 <see cref="System.Drawing.Font"/> 的 family。</param>
    /// <param name="Declared">声明原文（降级日志用）。</param>
    /// <param name="FellBack">是否回落到默认族。</param>
    public readonly record struct Resolution(string Family, string Declared, bool FellBack);

    /// <summary>声明值（可含逗号候选）→ 本机已安装的第一个 family；全部不可用 ⇒ 默认族 + 可见降级。</summary>
    /// <param name="declared">声明原文（可空）。</param>
    public static Resolution Resolve(string? declared)
    {
        var fallback = FrontEndDrivenConstants.DefaultFontFamily;
        var text = declared?.Trim() ?? string.Empty;
        if (text.Length == 0) return new Resolution(fallback, text, false);

        foreach (var raw in text.Split(','))
        {
            var candidate = raw.Trim().Trim('"', '\'').Trim();
            if (candidate.Length == 0) continue;
            if (GenericFamilies.Contains(candidate)) continue;
            if (IsInstalled(candidate)) return new Resolution(candidate, text, false);
        }

        if (Logged.TryAdd(text, 0))
        {
            FrontEndDrivenLog.Log("FontFallback", new { declared = text, used = fallback });
        }

        return new Resolution(fallback, text, true);
    }

    /// <summary>便捷入口：只要 family 值。</summary>
    /// <param name="declared">声明原文（可空）。</param>
    public static string ResolveFamily(string? declared) => Resolve(declared).Family;

    /// <summary>本机是否安装该 family（进程内缓存一次；字体安装属进程级外部事实）。</summary>
    /// <param name="family">family 名。</param>
    public static bool IsInstalled(string family)
    {
        if (string.IsNullOrWhiteSpace(family)) return false;
        EnsureLoaded();
        lock (Gate) return _installed!.Contains(family);
    }

    private static void EnsureLoaded()
    {
        if (_installed is not null) return;
        lock (Gate)
        {
            if (_installed is not null) return;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var fonts = new InstalledFontCollection();
                foreach (var family in fonts.Families) set.Add(family.Name);
            }
            catch (Exception ex)
            {
                // 枚举失败不静默：所有候选按未命中处理（回落默认族），并记一条可见降级。
                FrontEndDrivenLog.Log("FontFallback", new
                {
                    declared = "*",
                    used = FrontEndDrivenConstants.DefaultFontFamily,
                    reason = "enumeration-failed:" + ex.GetType().Name,
                });
            }

            _installed = set;
        }
    }
}
