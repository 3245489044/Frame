#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 声明 → CSS 投影器（**唯一 CSS 生成点**）：把已解析的 token/单元样式投影成层叠样式文本。
/// <para>
/// 输出两段：① <c>:root</c> 下 <c>--ui-*</c> 变量（token 语义值，单位less + 载体单位）；
/// ② 每个单元按声明 <c>html.cls</c> 生成类规则（槽位 → cssvar 变量），使声明成为外观的唯一来源。
/// </para>
/// <para>纪律：本类不含任何具体选择器/色值（全部来自声明）；未声明 <c>cls</c> 的单元不产出规则。</para>
/// </summary>
public static class SurfaceCssProjector
{
    /// <summary>样式模块名（注册到 <c>HtmlAssetRegistry.RegisterStyleModule</c>）。</summary>
    public const string ModuleName = "ui-declaration";

    /// <summary>CSS 载体 id（声明面键）。</summary>
    public const string CssCarrier = "html";

    /// <summary>按已解析皮肤投影 CSS（token 变量 + 单元类规则）。</summary>
    /// <param name="resolved">已解析渲染上下文。</param>
    public static string Project(ResolvedUi resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        var builder = new StringBuilder();

        builder.Append(":root{");
        foreach (var pair in resolved.Tokens.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append(Var(pair.Key)).Append(':').Append(pair.Value).Append(';');
        }

        builder.Append('}');

        foreach (var name in resolved.PartNames)
        {
            var part = resolved.Part(name);
            if (part is null) continue;

            var selector = Selector(part.Structure(CssCarrier));
            if (selector.Length == 0) continue;

            var declarations = new List<string>();
            foreach (var visual in part.Visuals(CssCarrier))
            {
                if (!string.Equals(visual.Bind, "cssvar", StringComparison.Ordinal)) continue;
                if (visual.Prop.Length == 0) continue;
                declarations.Add($"{visual.Prop}:{WithUnit(visual.Value, visual.Unit)}");
            }

            if (declarations.Count == 0) continue;
            builder.Append(selector).Append('{').Append(string.Join(";", declarations)).Append('}');
        }

        return builder.ToString();
    }

    /// <summary>便捷重载：声明 + 皮肤 → CSS。</summary>
    /// <param name="declaration">UI 声明。</param>
    /// <param name="skin">皮肤 id（可空 ⇒ 声明缺省皮肤）。</param>
    public static string Project(UiDeclaration declaration, string? skin = null)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return Project(declaration.Resolve(skin));
    }

    /// <summary>token 名 → CSS 变量名（下划线转连字符）。</summary>
    private static string Var(string token) => "--ui-" + token.Replace('_', '-');

    /// <summary>单元结构 → 选择器（cls 词以点连接；非法字符滤掉）。</summary>
    private static string Selector(IReadOnlyDictionary<string, string>? structure)
    {
        if (structure is null || !structure.TryGetValue("cls", out var cls)) return string.Empty;
        var classes = cls.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(IsSafeToken)
            .ToList();
        return classes.Count == 0 ? string.Empty : "." + string.Join(".", classes);
    }

    private static bool IsSafeToken(string token)
    {
        foreach (var ch in token)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '_') return false;
        }

        return token.Length > 0;
    }

    /// <summary>单位拼接（多值按空白分隔逐个追加；单位空 ⇒ 原样）。</summary>
    private static string WithUnit(string value, string unit)
        => unit.Length == 0
            ? value
            : string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => v + unit));
}