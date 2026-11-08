#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Plate.JsonMethod.Serialization;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 表面键规范化（**单源**）：层参数摘要 + 值文本化。
/// <para>吸收 <c>SkinCacheKey.ParamDigest</c> 与 <c>RenderPlanFingerprint.Canonical</c>；
/// 摘要文本与旧实现逐字相同（键序排序，禁枚举顺序当语义）。</para>
/// </summary>
public static class SurfaceKeyCanonical
{
    /// <summary>层参数摘要（角色/图元/z/全部参数；按键序，禁枚举顺序当语义）。</summary>
    /// <param name="layers">层栈。</param>
    public static string ParamDigest(IReadOnlyList<ResolvedLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var builder = new StringBuilder();
        foreach (var layer in layers)
        {
            if (builder.Length > 0) builder.Append('/');
            builder.Append(layer.Role).Append(':').Append(layer.Primitive).Append(':').Append(layer.Z).Append(':');
            var ordered = new List<KeyValuePair<string, string>>(layer.Params);
            ordered.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
            foreach (var pair in ordered)
            {
                builder.Append(pair.Key).Append('=').Append(pair.Value).Append(',');
            }
        }

        return builder.ToString();
    }

    /// <summary>值 → 稳定文本（字符串换行转义；可格式化值走 InvariantCulture）。
    /// <para><c>null</c> 与空串**必须可区分**（否则计划指纹碰撞）：<c>null ⇒ "\0null"</c>，空串 ⇒ 空串。</para></summary>
    /// <param name="value">待规范化值。</param>
    public static string CanonicalValue(object? value)
        => value switch
        {
            null => "\0null",
            string text => text.Replace("\n", "\\n", StringComparison.Ordinal),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => ConversationJson.Serialize(value, JsonSerializerFactory.BclDefault),
        };
}
