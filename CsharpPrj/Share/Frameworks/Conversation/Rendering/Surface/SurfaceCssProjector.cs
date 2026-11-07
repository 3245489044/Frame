#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UiBackend;

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

    /// <summary>
    /// 声明 token → 载体 CSS 变量别名（**值不在此**，只给名字与单位）。
    /// <para>用途：框架 CSS 仍以 <c>var(--bg)</c> 取名，但值只来自 <c>ui.ushar</c>；删掉旧 <c>:root</c> 后仍可解析。</para>
    /// </summary>
    private static readonly (string Token, string Variable, string Unit)[] AliasVariables =
    {
        ("text_primary", "--fg", string.Empty),
        ("text_agent", "--agent-fg", string.Empty),
        ("text_user", "--user-fg", string.Empty),
        ("text_muted", "--muted", string.Empty),
        ("surface_plain", "--bg", string.Empty),
        ("surface_agent", "--agent-bg", string.Empty),
        ("surface_user", "--user-bg", string.Empty),
        ("surface_muted", "--muted-bg", string.Empty),
        ("border_default", "--border", string.Empty),
        ("accent_primary", "--accent", string.Empty),
        ("radius_md", "--radius", "px"),
        ("pad_md", "--pad", "px"),
        ("gap_sm", "--gap", "px"),
        ("font_ui", "--font", string.Empty),
        ("size_body", "--size", "px"),
        ("ok", "--ok", string.Empty),
        ("info", "--info", string.Empty),
        ("danger", "--danger", string.Empty),
        ("warn", "--warn", string.Empty),
        ("status_idle", "--status-idle", string.Empty),
        ("status_done", "--status-done", string.Empty),
        ("status_cancelled", "--status-cancelled", string.Empty),
        ("error_bg", "--error-bg", string.Empty),
        ("error_fg", "--error-fg", string.Empty),
    };

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

        // 载体变量别名（值单源 = 声明；CSS/组件只读 var(--bg) 等，不再自带值）。
        foreach (var alias in AliasVariables)
        {
            if (!resolved.Tokens.TryGetValue(alias.Token, out var value) || value.Length == 0) continue;
            builder.Append(alias.Variable).Append(':').Append(CssValue.AppendUnit(value, alias.Unit)).Append(';');
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
                declarations.Add($"{visual.Prop}:{CssValue.AppendUnit(visual.Value, visual.Unit)}");
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

    /// <summary>
    /// 便捷重载：声明 + 表面程序 → CSS（皮肤取档位声明的 <c>values.ui_skin</c>）。
    /// <para>玻璃档声明 <c>ui_skin=dark</c> 时，token 变量（<c>--bg/--fg/--agent-bg…</c>）必须按暗档投影，
    /// 否则 HTML 内容块仍按缺省 light 皮肤出白底。档未声明/无 <c>ui_skin</c> ⇒ 回落声明缺省皮肤（零回归）。</para>
    /// </summary>
    /// <param name="declaration">UI 声明。</param>
    /// <param name="program">表面程序（可空 ⇒ 回落声明缺省皮肤）。</param>
    /// <param name="profileId">外观档（可空 ⇒ <see cref="SurfaceProgramDeclaration.ActiveProfile"/>）。</param>
    public static string Project(
        UiDeclaration declaration,
        SurfaceProgramDeclaration? program,
        string? profileId = null)
    {
        var id = string.IsNullOrWhiteSpace(profileId) ? program?.ActiveProfile : profileId;
        var skin = program?.EffectiveProfile(id ?? string.Empty)?.Values.TryGetValue("ui_skin", out var value) == true
            ? value
            : null;
        return Project(declaration, string.IsNullOrWhiteSpace(skin) ? null : skin);
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
}
