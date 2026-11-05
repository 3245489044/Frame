using System;
using System.Collections.Generic;
using System.Text;

namespace Modules.Node.Identity;

/// <summary>
/// 位置键模板 → **读前缀**（唯一实现）：只取到首个未知占位符之前的最长字面段，
/// 已知身份占位符（container / round）先代入。
/// <para>
/// 背景：读投影此前把「身份模板」当前缀来源，单键域身份模板（<c>{container}</c>）恒派生出空前缀
/// ⇒ 载体全量枚举 + 跨域混读。前缀必须由**位置键模板**派生（<c>proc.{container}</c> ⇒ <c>proc.</c>）。
/// </para>
/// <para>
/// 纪律：不可判（模板以未知占位符起始）⇒ 返回 <c>null</c>，调用方据此计 <c>prefix-undefined</c>，
/// 禁止猜测或回退通配。
/// </para>
/// </summary>
public static class PlacementKeyPrefix
{
    /// <summary>派生读前缀。</summary>
    /// <param name="keyTemplate">位置键模板（<c>PlacementSpec.KeyTemplate</c>）。</param>
    /// <param name="values">已知身份值（键 = 占位符名；缺省 = 未知）。</param>
    public static string? LiteralPrefixOf(
        string? keyTemplate, IReadOnlyDictionary<string, string?>? values = null)
    {
        if (string.IsNullOrWhiteSpace(keyTemplate)) return null;

        var sb = new StringBuilder();
        for (var i = 0; i < keyTemplate.Length; i++)
        {
            var ch = keyTemplate[i];
            if (ch == '{')
            {
                var end = keyTemplate.IndexOf('}', i + 1);
                if (end < 0) break;
                var name = keyTemplate.Substring(i + 1, end - i - 1).Trim();
                if (values is not null
                    && values.TryGetValue(name, out var value)
                    && !string.IsNullOrEmpty(value))
                {
                    sb.Append(value);
                    i = end;
                    continue;
                }

                break;
            }

            if (ch == '}') break;
            sb.Append(ch);
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>便捷入口：以容器 / 轮次两项已知身份派生。</summary>
    public static string? LiteralPrefixOf(string? keyTemplate, string? container, int? round)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(container)) values["container"] = container;
        if (round is { } r) values["round"] = r.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return LiteralPrefixOf(keyTemplate, values);
    }
}
