#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

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

    /// <summary>为给定材质类生成 <c>::after</c> 光谱层；无类名 ⇒ 空串（不注册）。</summary>
    public static string Build(IReadOnlyList<string> classNames)
    {
        if (classNames is null || classNames.Count == 0) return string.Empty;

        var builder = new StringBuilder(classNames.Count * 460);
        // 模块级缺省（材质规则会按声明覆盖）：`--sl-spectral-w` 必须始终有值（作 border-width 使用）。
        builder.Append(":root{--sl-spectral-w:6px;--sl-spectral-a:0.5}\n");
        foreach (var className in classNames)
        {
            // ::after 需要定位上下文；isolation:isolate 让负层级的虹彩只压在本材质自身上（不穿透到页面）。
            // 只在本模块内给光谱材质补定位/隔离（不污染其它材质）。
            builder.Append('.').Append(className).Append("{position:relative;isolation:isolate}\n");
            builder.Append('.').Append(className).Append("::after{")
                .Append("content:\"\";position:absolute;inset:0;box-sizing:border-box;")
                .Append("border-radius:inherit;pointer-events:none;")
                // 负层级：色带落在材质**内容之下、填充之上**，不再盖住文字/表格（历史表格读不出的直接原因）。
                .Append("z-index:-1;")
                // 环宽 = 材质声明 width（border 提供**恒定宽度**外框，不随元素长宽比变形）。
                // 旧 `radial-gradient(closest-side …)` 椭圆环在四角会增厚（实测 6px → 23px、铺满 25.94%）。
                .Append("border:var(--sl-spectral-w) solid transparent;")
                // 色相环：固定起点 + 多色标，保证沿轮廓连续且跨度 ≥90°。
                // at 用视口 px 锚点 + fixed 定位区 ⇒ 色相由「面板相对光斑的位置」决定（不是元素自身百分比）。
                .Append("background:conic-gradient(from 135deg at var(--sl-anchor-x,30vw) var(--sl-anchor-y,18vh),")
                .Append("#7fe9ff 0deg,#8f8cff 72deg,#ff8ae0 144deg,#ffd59a 216deg,#6f7bff 288deg,#7fe9ff 360deg);")
                .Append("background-attachment:fixed;background-clip:border-box;")
                // 只留 border 区：padding-box 挖掉 border-box ⇒ 得到声明带宽的窄环（union 相减，非径向近似）。
                .Append("mask:linear-gradient(#000 0 0) padding-box,linear-gradient(#000 0 0) border-box;")
                .Append("-webkit-mask:linear-gradient(#000 0 0) padding-box,linear-gradient(#000 0 0) border-box;")
                // 标准属性写在最后：`mask` 简写会重置 composite，旧引擎只认 -webkit-。
                .Append("-webkit-mask-composite:xor;mask-composite:exclude;")
                // 峰值由材质声明 strength × 光态（两载体同一 SheenAlpha 公式）；不是写死的恒亮。
                .Append("opacity:var(--sl-spectral-a);")
                // 全局相位 = 锚点相对覆盖区的方位角，只由设施位置/光斑锚点决定。
                .Append("filter:hue-rotate(var(--sl-irid-angle,0deg));")
                .Append("}\n");
        }

        return builder.ToString();
    }
}
