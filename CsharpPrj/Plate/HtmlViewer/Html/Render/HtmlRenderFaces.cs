#nullable enable

using System;
using System.Collections.Generic;

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

    /// <summary>渲染未就绪面（就绪矩阵 + 能力键注记）：正文/注记均经编码。</summary>
    public static string NotReady(string title, string readinessText, string? capability)
        => HtmlBlocks.Notice(
            "empty",
            title,
            readinessText,
            string.IsNullOrEmpty(capability) ? null : "capability: " + capability);

    /// <summary>渲染失败面（可归因文案；异常分类由调用方完成后传入）。</summary>
    public static string Failed(IReadOnlyDictionary<string, string> templates, string label, string message)
        => HtmlEmptyState.Render(templates, "（" + label + "：" + message + "）");
}
