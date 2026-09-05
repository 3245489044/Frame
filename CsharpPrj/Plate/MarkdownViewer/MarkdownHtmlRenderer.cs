using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace Plate.MarkdownViewer;

/// <summary>
/// Markdown → HTML 渲染：Markdig 解析（CommonMark + 表格/删除线等进阶扩展），关闭原始 HTML 通道防注入，
/// 再包一层内嵌 CSS + JS 的完整 HTML 页（表格横滚、JSON 语法高亮、代码块复制/语言标签、浅/深色主题）。
/// 纯函数，供 <see cref="MarkdownViewer"/> 控件与测试复用。
/// </summary>
public static class MarkdownHtmlRenderer
{
    // 关闭原始 HTML 通道（DisableHtml）：任务载荷/用户输入里的 <script> 等不得进入 DOM（XSS 防线）。
    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private static readonly Regex JsonCodeBlock =
        new("<pre><code class=\"language-json\">(.*?)</code></pre>", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Markdown 源文本 → 完整 HTML 页面（含内嵌 CSS 的文档壳）。空/null 输入返回空文档。</summary>
    /// <param name="markdown">Markdown 源文本。</param>
    /// <param name="fontSizePx">正正文档基础字号（px）。默认 null = 使用 <see cref="DefaultCss"/> 中的 15px；传正值覆盖。</param>
    public static string ToMarkdownHtml(string? markdown, double? fontSizePx = null)
    {
        var body = Markdig.Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        body = HighlightJsonBlocks(body);
        // 字号覆盖：内联一层后置 style（晚于 DefaultCss 定义，保证覆盖其 body 字号）。
        var fontStyle = fontSizePx is > 0
            ? $"<style>body{{font-size:{fontSizePx.Value:F1}px;}}</style>"
            : string.Empty;
        return $"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<style>
{DefaultCss}
</style>
{fontStyle}
</head>
<body>{body}
<script>
{InitScript}
</script>
</body>
</html>
""";
    }
    /// <summary>
    /// 将 ```json 围栏代码块内的内容做 JSON 语法高亮（键/字符串/数字/布尔/空值 → 带 class 的 span）。
    /// 内容先经 Markdig 整体 HTML 转义，此处解码后逐 token 重新转义，杜绝引入用户 HTML。
    /// </summary>
    internal static string HighlightJsonBlocks(string body)
    {
        if (!body.Contains("language-json", StringComparison.Ordinal))
            return body;
        return JsonCodeBlock.Replace(body, m =>
        {
            var code = WebUtility.HtmlDecode(m.Groups[1].Value);
            return $"<pre><code class=\"language-json\">{HighlightJson(code)}</code></pre>";
        });
    }

    /// <summary>
    /// JSON 文本 → 带高亮 span 的转义 HTML。纯状态机 token 化，每个 token 均经 HtmlEncode，
    /// 输出仅含本类自产 span（无原始 HTML 透传），与 <see cref="Pipeline"/> 的 DisableHtml 防线一致。
    /// </summary>
    public static string HighlightJson(string json)
    {
        if (string.IsNullOrEmpty(json))
            return string.Empty;

        var sb = new StringBuilder(json.Length + 64);
        int i = 0, n = json.Length;
        while (i < n)
        {
            char c = json[i];
            if (c == '"')
            {
                int start = i++;
                while (i < n)
                {
                    if (json[i] == '\\') { i += 2; continue; } // 转义跳 2 字符
                    if (json[i] == '"') { i++; break; }
                    i++;
                }
                // 引号串后紧跟冒号（忽略空白）→ 键；否则为字符串值
                int look = i;
                while (look < n && char.IsWhiteSpace(json[look])) look++;
                bool isKey = look < n && json[look] == ':';
                sb.Append(Enc(json.Substring(start, i - start), isKey ? "j-key" : "j-str"));
            }
            else if (c == '-' || (c >= '0' && c <= '9'))
            {
                int start = i++;
                while (i < n && (char.IsAsciiDigit(json[i]) || json[i] == '.'
                    || json[i] == 'e' || json[i] == 'E' || json[i] == '+' || json[i] == '-')) i++;
                sb.Append(Enc(json.Substring(start, i - start), "j-num"));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(json[i]) || json[i] == '_')) i++;
                string word = json.Substring(start, i - start);
                sb.Append(word switch
                {
                    "true" or "false" => Enc(word, "j-bool"),
                    "null"            => Enc(word, "j-null"),
                    _                 => WebUtility.HtmlEncode(word), // 非法标识符原样转义，不高亮
                });
            }
            else
            {
                // 标点/空白段（非任何 token 起始）
                int start = i++;
                while (i < n && !IsTokenStart(json[i])) i++;
                sb.Append(WebUtility.HtmlEncode(json.Substring(start, i - start)));
            }
        }
        return sb.ToString();
    }

    private static bool IsTokenStart(char c) =>
        c == '"' || c == '-' || (c >= '0' && c <= '9') || char.IsLetter(c) || c == '_';

    private static string Enc(string s, string cls) =>
        $"<span class=\"{cls}\">{WebUtility.HtmlEncode(s)}</span>";


    /// <summary>
    /// 默认主题 CSS：CSS 变量 + 浅/深双主题（跟随系统 prefers-color-scheme）；表格横滚、JSON 高亮配色、
    /// 代码块语言标签 + 复制按钮、CJK 友好排版。
    /// </summary>
    public const string DefaultCss =
        """
        :root {
            --bg: #ffffff;
            --fg: #24292f;
            --muted: #57606a;
            --border: #d0d7de;
            --head-bg: #f6f8fa;
            --stripe: #f6f8fa;
            --code-bg: #f6f8fa;
            --pre-bg: #0d1117;
            --pre-fg: #c9d1d9;
            --link: #0969da;
            --shadow: 0 1px 3px rgba(0, 0, 0, .08);
        }
        @media (prefers-color-scheme: dark) {
            :root {
                --bg: #0d1117;
                --fg: #e6edf3;
                --muted: #8b949e;
                --border: #30363d;
                --head-bg: #161b22;
                --stripe: #161b22;
                --code-bg: #161b22;
                --pre-bg: #010409;
                --pre-fg: #c9d1d9;
                --link: #58a6ff;
                --shadow: 0 1px 3px rgba(0, 0, 0, .4);
            }
        }
        * { box-sizing: border-box; }
        body {
            font-family: "Segoe UI", "Microsoft YaHei", sans-serif;
            margin: 0; padding: 20px 22px;
            font-size: 15px; line-height: 1.7;
            color: var(--fg); background: var(--bg);
            word-wrap: break-word;
        }
        h1, h2, h3, h4, h5, h6 { line-height: 1.3; margin: 1.2em 0 .5em; font-weight: 600; }
        h1 { font-size: 1.7em; border-bottom: 2px solid var(--border); padding-bottom: .3em; }
        h2 { font-size: 1.4em; border-bottom: 1px solid var(--border); padding-bottom: .25em; }
        h3 { font-size: 1.2em; }
        h4 { font-size: 1.05em; }
        h1:first-child, h2:first-child, h3:first-child { margin-top: 0; }
        p { margin: .6em 0; }
        ul, ol { margin: .6em 0; padding-left: 1.6em; }
        li { margin: .2em 0; }
        a { color: var(--link); text-decoration: none; }
        a:hover { text-decoration: underline; }
        hr { border: none; border-top: 1px solid var(--border); margin: 1.4em 0; }
        blockquote {
            border-left: 4px solid var(--border); margin: .8em 0; padding: 4px 14px;
            color: var(--muted); background: var(--stripe);
        }
        /* ── 表格：横滚容器 + 单元格自动换行，防超宽字段撑破 ── */
        .table-wrap { overflow-x: auto; margin: 10px 0; }
        table { border-collapse: collapse; width: 100%; min-width: 0; }
        th, td {
            border: 1px solid var(--border); padding: 6px 12px; text-align: left;
            word-break: break-word; overflow-wrap: anywhere; min-width: 0;
        }
        th { background: var(--head-bg); font-weight: 600; }
        tr:nth-child(even) { background: var(--stripe); }
        /* ── 代码：行内 + 块级（深底、横向滚动、头部条） ── */
        code {
            font-family: Consolas, "Cascadia Code", monospace;
            font-size: .9em;
            background: var(--code-bg); padding: 2px 5px; border-radius: 4px;
        }
        pre {
            background: var(--pre-bg); color: var(--pre-fg);
            padding: 0; border-radius: 8px; overflow: hidden;
            margin: 12px 0; box-shadow: var(--shadow);
        }
        pre code { background: transparent; color: inherit; padding: 12px 14px; display: block; overflow-x: auto; }
        .code-head {
            display: flex; justify-content: space-between; align-items: center;
            padding: 4px 10px; background: rgba(127, 127, 127, .14);
            border-bottom: 1px solid rgba(127, 127, 127, .18);
            font-size: 12px;
        }
        .code-lang { text-transform: uppercase; letter-spacing: .04em; color: var(--muted); }
        .copy-btn {
            border: 1px solid var(--border); background: var(--bg); color: var(--fg);
            border-radius: 4px; padding: 1px 10px; font-size: 12px; cursor: pointer;
        }
        .copy-btn:hover { background: var(--head-bg); }
        /* ── JSON 语法高亮配色（浅/深各一套） ── */
        .j-key { color: #0550ae; }
        .j-str { color: #a31515; }
        .j-num { color: #098658; }
        .j-bool, .j-null { color: #0000c8; }
        @media (prefers-color-scheme: dark) {
            .j-key { color: #4ec9b0; }
            .j-str { color: #ce9178; }
            .j-num { color: #b5cea8; }
            .j-bool, .j-null { color: #569cd6; }
        }
        """;


    /// <summary>
    /// 文档壳 JS：表格包横滚容器、代码块注入语言标签 + 复制按钮。复制优先 Clipboard API，回退 execCommand。
    /// </summary>
    public const string InitScript =
        """
        function initDocs() {
          document.querySelectorAll('table').forEach(function (t) {
            if (t.parentElement && t.parentElement.classList.contains('table-wrap')) return;
            var w = document.createElement('div'); w.className = 'table-wrap';
            t.parentNode.insertBefore(w, t); w.appendChild(t);
          });
          document.querySelectorAll('pre').forEach(function (pre) {
            if (pre.querySelector('.code-head')) return;
            var code = pre.querySelector('code'); if (!code) return;
            var lang = '';
            var m = (code.className || '').match(/language-([\w-]+)/);
            if (m) lang = m[1];
            var head = document.createElement('div'); head.className = 'code-head';
            var lab = document.createElement('span'); lab.className = 'code-lang'; lab.textContent = lang;
            head.appendChild(lab);
            var btn = document.createElement('button');
            btn.type = 'button'; btn.className = 'copy-btn'; btn.textContent = '复制';
            btn.addEventListener('click', function () {
              var text = code.innerText;
              function flash() { btn.textContent = '✓ 已复制'; setTimeout(function () { btn.textContent = '复制'; }, 1200); }
              function fallback() {
                var ta = document.createElement('textarea');
                ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
                document.body.appendChild(ta); ta.select();
                try { document.execCommand('copy'); flash(); } catch (e) { }
                document.body.removeChild(ta);
              }
              if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text).then(flash).catch(fallback);
              } else { fallback(); }
            });
            head.appendChild(btn);
            pre.insertBefore(head, code);
          });
        }
        initDocs();
        """;
}

