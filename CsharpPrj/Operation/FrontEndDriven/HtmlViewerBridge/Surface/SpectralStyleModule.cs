#nullable enable

using System.Collections.Generic;
using UiBackend;
using UiBackend.Css;

namespace FrontEndDriven.HtmlViewerBridge.Surface;

/// <summary>
/// 光谱高光的**独立样式模块**（与 <see cref="CssSurfaceProjector"/> 的材质声明分开，降低与其它改动的冲突）。
/// <para>
/// 只消费两类输入：需要光谱的材质类名（由投影器标记）与 <c>:root</c> 上的
/// <c>--sl-irid-angle</c>（虹彩相位 = 世界固定锚点相对面板的方位角）。样式正文由本文件拥有，
/// <see cref="CssSurfaceProjector"/> 不拼接 <c>::after</c> 规则。
/// </para>
/// <para>
/// 色相环同样锚在屏幕：<c>background-attachment:fixed</c> + px 锚点使 conic 的定位区 = 视口，
/// 面板从光斑下方穿过时采样到的色相随之变化，而不是退回元素自身盒子的局部百分比。
/// </para>
/// </summary>
public static class SpectralStyleModule
{
    /// <summary>模块名（经 <c>HtmlAssetRegistry.RegisterStyleModule</c> 注册）。</summary>
    public const string ModuleName = "surface-spectral";

    /// <summary>
    /// 色相环：令牌色相 ± span/2 + 世界相位（与 WinForms IridescenceRamp 同一公式）。
    /// 旧实现是固定六色 + hue-rotate，无法与令牌/配置对齐；现在色标由 <c>--sl-spectral-hue/span</c> 生成。
    /// </summary>
    private static readonly string RingGradient = string.Join(string.Empty, new[]
    {
        "conic-gradient(from 135deg at var(--sl-anchor-x,30vw) var(--sl-anchor-y,18vh),",
        "hsl(calc(var(--sl-spectral-hue,190deg) + var(--sl-irid-angle,0deg) - var(--sl-spectral-span,180deg) * 0.5),85%,78%) 0deg,",
        "hsl(calc(var(--sl-spectral-hue,190deg) + var(--sl-irid-angle,0deg) - var(--sl-spectral-span,180deg) * 0.3),85%,78%) 72deg,",
        "hsl(calc(var(--sl-spectral-hue,190deg) + var(--sl-irid-angle,0deg) - var(--sl-spectral-span,180deg) * 0.1),85%,78%) 144deg,",
        "hsl(calc(var(--sl-spectral-hue,190deg) + var(--sl-irid-angle,0deg) + var(--sl-spectral-span,180deg) * 0.1),85%,78%) 216deg,",
        "hsl(calc(var(--sl-spectral-hue,190deg) + var(--sl-irid-angle,0deg) + var(--sl-spectral-span,180deg) * 0.3),85%,78%) 288deg,",
        "hsl(calc(var(--sl-spectral-hue,190deg) + var(--sl-irid-angle,0deg) + var(--sl-spectral-span,180deg) * 0.5),85%,78%) 360deg)",
    });

    /// <summary>只留 border 区：padding-box 挖掉 border-box ⇒ 得到声明带宽的窄环（union 相减，非径向近似）。</summary>
    private const string RingMask =
        "linear-gradient(#000 0 0) padding-box,linear-gradient(#000 0 0) border-box";

    /// <summary>为给定材质类生成 <c>::after</c> 光谱层；无类名 ⇒ 空串（不注册）。</summary>
    public static string Build(IReadOnlyList<string> classNames)
    {
        if (classNames is null || classNames.Count == 0) return string.Empty;

        var rules = new List<string>(1 + classNames.Count * 2)
        {
            // 模块级缺省（材质规则会按声明覆盖）：`--sl-spectral-w` 必须始终有值（作 border-width 使用）。
            CompactRule(":root", Pair("--sl-spectral-w", "6px"), Pair("--sl-spectral-a", "0.5")),
        };

        foreach (var className in classNames)
        {
            var selector = CssSelector.Class(className);
            // ::after 需要定位上下文；isolation:isolate 让负层级的虹彩只压在本材质自身上（不穿透到页面）。
            // 只在本模块内给光谱材质补定位/隔离（不污染其它材质）。
            rules.Add(CompactRule(selector,
                Pair("position", "relative"),
                Pair("isolation", "isolate")));
            rules.Add(Rule($"{selector}::after",
                Pair("content", "\"\""),
                Pair("position", "absolute"),
                Pair("inset", "0"),
                Pair("box-sizing", "border-box"),
                Pair("border-radius", "inherit"),
                Pair("pointer-events", "none"),
                // 负层级：色带落在材质**内容之下、填充之上**，不再盖住文字/表格（历史表格读不出的直接原因）。
                Pair("z-index", "-1"),
                // 环宽 = 材质声明 width（border 提供**恒定宽度**外框，不随元素长宽比变形）。
                // 旧 `radial-gradient(closest-side …)` 椭圆环在四角会增厚（实测 6px → 23px、铺满 25.94%）。
                Pair("border", "var(--sl-spectral-w) solid transparent"),
                Pair("background", RingGradient),
                Pair("background-attachment", "fixed"),
                Pair("background-clip", "border-box"),
                Pair("mask", RingMask),
                Pair("-webkit-mask", RingMask),
                // 标准属性写在最后：`mask` 简写会重置 composite，旧引擎只认 -webkit-。
                Pair("-webkit-mask-composite", "xor"),
                Pair("mask-composite", "exclude"),
                // 峰值由材质声明 strength × 光态（两载体同一 SheenAlpha 公式）；不是写死的恒亮。
                // 色相相位已在上面的 hsl 中叠加 --sl-irid-angle；此处不得再 hue-rotate（否则 2× 旋转）。
                Pair("opacity", "var(--sl-spectral-a)")));
        }

        return $"{string.Join("\n", rules)}\n";
    }

    /// <summary>单条规则文本（复用 plate 的 CSS 文本构建器：属性序保留、空值跳过、统一 <c>prop:value;</c>）。</summary>
    private static string Rule(string selector, params KeyValuePair<string, string>[] declarations)
        => new CssDocumentBuilder().Rule(selector, declarations).Build();

    /// <summary>末条声明不带分号的规则（保持本模块既有产物形态；其余语义与 <see cref="Rule"/> 相同）。</summary>
    private static string CompactRule(string selector, params KeyValuePair<string, string>[] declarations)
    {
        var text = Rule(selector, declarations);
        return text.Length > 1 && text[^2] == ';' ? text.Remove(text.Length - 2, 1) : text;
    }

    private static KeyValuePair<string, string> Pair(string property, string value) => new(property, value);
}
