#nullable enable

using System;
using System.Net;
using System.Text;

namespace Modules.Parsing.Markup;

/// <summary>
/// 未编码纯文本（<b>需要编码</b>）。<see cref="string"/> 可隐式转入（默认语义 = 待编码文本）。
/// <para>纪律：本类型**不能**由 <see cref="UiMarkup"/> 隐式构造（禁把已编码片段当文本再编码一次）。</para>
/// </summary>
public readonly record struct UiText(string Value)
{
    /// <summary>字符串 ⇒ 待编码文本（唯一隐式入口）。</summary>
    public static implicit operator UiText(string? value) => new(value ?? string.Empty);

    /// <summary>是否空文本。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// 已编码 HTML 片段（<b>受信原文</b>，入 DOM 不再转义）。
/// <para>与 <see cref="UiText"/> **不互转**：把片段塞进文本位（或反之）在编译期即失败——
/// 这是「二次转义 / 片段当文本显示」缺陷的类型级屏障。</para>
/// <para>编码点唯一: <see cref="FromText"/>；组合唯一: <see cref="Concat"/>。</para>
/// </summary>
public readonly record struct UiMarkup
{
    private readonly string? _html;

    private UiMarkup(string? html) => _html = html;

    /// <summary>已编码 HTML（序列化/模板 <c>|raw</c> 位唯一读出点）。</summary>
    public string Html => _html ?? string.Empty;

    /// <summary>是否空片段。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(_html);

    /// <summary>空片段。</summary>
    public static UiMarkup Empty => default;

    /// <summary>受信片段（调用方保证已编码；仅限框架产出/外部受信入口）。</summary>
    public static UiMarkup Trusted(string? encodedHtml) => new(encodedHtml);

    /// <summary>纯文本 ⇒ 片段（**唯一编码点**）。</summary>
    public static UiMarkup FromText(string? text) => new(WebUtility.HtmlEncode(text ?? string.Empty));

    /// <summary>
    /// 片段组合：<see cref="UiMarkup"/> 原样、<see cref="UiText"/>/<see cref="string"/> 编码后拼接。
    /// 禁在调用方手写「先 Encode 再拼」。
    /// </summary>
    public static UiMarkup Concat(params object?[] parts)
    {
        if (parts is null || parts.Length == 0) return Empty;
        var sb = new StringBuilder(64);
        foreach (var part in parts)
        {
            switch (part)
            {
                case null:
                    break;
                case UiMarkup markup:
                    sb.Append(markup.Html);
                    break;
                case UiText text:
                    sb.Append(WebUtility.HtmlEncode(text.Value));
                    break;
                case string raw:
                    sb.Append(WebUtility.HtmlEncode(raw));
                    break;
                default:
                    throw new ArgumentException(
                        $"UiMarkup.Concat 只接受 UiMarkup / UiText / string（收到 {part.GetType().FullName}）",
                        nameof(parts));
            }
        }

        return new UiMarkup(sb.ToString());
    }

    /// <summary>片段追加（同类型，不编码）。</summary>
    public UiMarkup Append(UiMarkup other) => new(Html + other.Html);

    /// <inheritdoc />
    public override string ToString() => Html;
}
