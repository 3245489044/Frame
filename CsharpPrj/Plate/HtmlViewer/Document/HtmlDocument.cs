#nullable enable

namespace Plate.HtmlViewer
{
    /// <summary>
    /// 通用 HTML 文档写作工具：转义 / 整页骨架 / 标题行 / 纯文本兜底页。
    /// 供各宿主（Produce、Agent 等）生成交给 HtmlViewer(NavigateToString 整页注入) 的整页字符串，
    /// 避免各处各自硬编码页面骨架/转义逻辑（原 Produce.Launcher 内联 Html 辅助类即下沉于此）。
    /// </summary>
    public static class HtmlDoc
    {
        /// <summary>HTML 转义（&amp; &lt; &gt; &quot; &#39;），防注入。</summary>
        public static string Escape(string? text) =>
            (text ?? string.Empty)
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&#39;");

        /// <summary>整页骨架：统一字体/内边距 + <c>.muted</c> 弱化样式，body 片段（须为已转义/自包含内容）直接注入。</summary>
        public static string Page(string body) =>
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
            "<style>html,body{margin:0;padding:12px 16px;font-family:'Segoe UI','Microsoft YaHei',sans-serif;color:#24292f}" +
            ".muted{color:#57606a;font-weight:normal;font-size:0.85em}" +
            "</style></head><body>" + body + "</body></html>";

        /// <summary>标题行：<c>&lt;h3&gt;title &lt;span class="muted"&gt;(subtitle)&lt;/span&gt;&lt;/h3&gt;</c>（均已转义）。</summary>
        public static string Heading(string title, string? subtitle = null) =>
            "<h3>" + Escape(title) +
            (string.IsNullOrEmpty(subtitle) ? "" : " <span class=\"muted\">(" + Escape(subtitle) + ")</span>") +
            "</h3>";

        /// <summary>纯文本兜底页（错误/降级时用）：仍是合法整页，文字经转义入 body。</summary>
        public static string Wrap(string title, string text) =>
            Page(Heading(title) + "<pre>" + Escape(text) + "</pre>");
    }
}