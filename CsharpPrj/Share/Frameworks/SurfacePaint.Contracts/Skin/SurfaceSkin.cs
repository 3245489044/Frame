#nullable enable

using System.Collections.Generic;

namespace SurfacePaint.Contracts;

/// <summary>
/// 唯一皮肤（两载体共用）：档位 + <c>ui_skin</c> + 已按档位取值的 token 集 + 指纹。
/// <para>纪律：载体不得再自行解析 <c>ThemeMode</c>/<c>prefers-color-scheme</c>；底色/文字色一律取 <see cref="Tokens"/>。</para>
/// </summary>
/// <param name="ProfileId">外观档 id（如 <c>glass_light_show</c>）。</param>
/// <param name="UiSkin">唯一模式来源（<c>light</c> | <c>dark</c> | <c>show</c>）。</param>
/// <param name="Tokens">已按档位取值的 token 集（键 = 声明 token 名，值 = 载体值）。</param>
/// <param name="Fingerprint">皮肤指纹（档位 + 令牌集摘要；用于缓存/门禁）。</param>
public sealed record SurfaceSkin(
    string ProfileId,
    string UiSkin,
    IReadOnlyDictionary<string, string> Tokens,
    string Fingerprint)
{
    /// <summary>取 token（缺失 ⇒ null）。</summary>
    public string? Token(string name) => Tokens.TryGetValue(name, out var value) ? value : null;
}

/// <summary>皮肤解析端口（唯一实现 = 声明层；载体只消费结果）。</summary>
public interface ISurfaceSkinSource
{
    /// <summary>解析当前皮肤（不可用 ⇒ null，调用方须可见降级）。</summary>
    SurfaceSkin? Resolve();
}
