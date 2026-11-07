#nullable enable

using System;
using System.Collections.Generic;
using Modules.Parsing.Markup;

namespace UiBackend.Html;

/// <summary>
/// 渲染面三态统一出口 —— 「空态 / 未就绪 / 失败」的单一实现。
/// <para>
/// 收敛各渲染器自写的空态片段、就绪矩阵与异常文案：空态经 <see cref="HtmlEmptyState"/>（模板 + 内嵌兜底），
/// 未就绪经 <see cref="HtmlBlocks.Notice"/>，失败统一为可归因文案。文本一律经引擎/块构建器编码（XSS 纪律一致）。
/// </para>
/// <para>零领域类型：能力键、标题、就绪文本、失败原因均由调用方给出。</para>
/// </summary>
public static class HtmlRenderFaces
{
    /// <summary>渲染空态（显式「空 ≠ 无」）：<paramref name="text"/> 经模板引擎编码。</summary>
    public static string Empty(IReadOnlyDictionary<string, string> templates, string text)
        => HtmlEmptyState.Render(templates, text);

    /// <summary>渲染空态为受信片段（单一实现 = <see cref="Empty"/>；业务侧不再手写 Trusted）。</summary>
    public static UiMarkup EmptyMarkup(IReadOnlyDictionary<string, string> templates, string text)
        => HtmlEmptyState.RenderMarkup(templates, text);

    /// <summary>渲染未就绪面（就绪矩阵 + 能力键注记）：正文/注记均经编码。</summary>
    public static string NotReady(
        string title,
        string readinessText,
        string? capability,
        string capabilityPrefix = "capability: ")
        => HtmlBlocks.Notice(
            "empty",
            title,
            readinessText,
            string.IsNullOrEmpty(capability) ? null : capabilityPrefix + capability);

    /// <summary>渲染失败面（可归因文案；异常分类由调用方完成后传入；格式串可注入）。</summary>
    public static string Failed(
        IReadOnlyDictionary<string, string> templates,
        string label,
        string message,
        string format = "（{0}：{1}）")
        => HtmlEmptyState.Render(templates, string.Format(System.Globalization.CultureInfo.InvariantCulture, format, label, message));
}
