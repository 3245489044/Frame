#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace UiBackend.Css;

/// <summary>
/// 确定性 CSS 文本构建器（载体方言落点，**无语义**）：只管转义/拼接/键序稳定，不认识任何材质或值。
/// <para>纪律：不含选择器字面量与色值（全部由调用方按声明给出）；同一输入必得同一文本。</para>
/// </summary>
public sealed class CssDocumentBuilder
{
    private readonly StringBuilder _builder = new();

    /// <summary>追加一条规则（选择器 + 声明表；空选择器/空声明 ⇒ 不产）。</summary>
    /// <param name="selector">选择器（如 <c>.material-bubble_glass</c>）。</param>
    /// <param name="declarations">属性 → 值（保序；空值跳过）。</param>
    public CssDocumentBuilder Rule(string selector, IEnumerable<KeyValuePair<string, string>> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var name = Safe(selector);
        if (name.Length == 0) return this;

        var body = new StringBuilder();
        foreach (var pair in declarations)
        {
            var property = Safe(pair.Key);
            var value = (pair.Value ?? string.Empty).Trim();
            if (property.Length == 0 || value.Length == 0) continue;
            body.Append(property).Append(':').Append(value).Append(';');
        }

        if (body.Length == 0) return this;
        _builder.Append(name).Append('{').Append(body).Append('}');
        return this;
    }

    /// <summary>追加已拼好的声明块（仅做空白归一；空 ⇒ 不产）。</summary>
    /// <param name="selector">选择器。</param>
    /// <param name="declarationBlock">已拼接的 <c>prop:value;</c> 串。</param>
    public CssDocumentBuilder RuleBlock(string selector, string declarationBlock)
    {
        var body = (declarationBlock ?? string.Empty).Trim();
        if (body.Length == 0) return this;
        if (!body.EndsWith(';')) body += ";";
        var name = Safe(selector);
        if (name.Length == 0) return this;
        _builder.Append(name).Append('{').Append(body).Append('}');
        return this;
    }

    /// <summary>产物（无内容 ⇒ 空串）。</summary>
    public string Build() => _builder.ToString();

    /// <summary>选择器/属性名净化（去除花括号/分号/换行，防注入与不可复算）。</summary>
    private static string Safe(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text.Trim())
        {
            if (ch is '{' or '}' or ';' or '\r' or '\n') continue;
            builder.Append(ch);
        }

        return builder.ToString();
    }
}
