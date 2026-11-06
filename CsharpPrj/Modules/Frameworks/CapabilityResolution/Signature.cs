#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Modules.CapabilityResolution;

/// <summary>
/// 规范签名（**唯一签名生成点**）：只读结构事实（primitive/z/params/state），排序确定，禁读执行者叙述。
/// <para>sig(layer)=primitive|z|params(sorted)|state；sig(material)=sha256(join(sig(layer) by z))。</para>
/// </summary>
public static class Signature
{
    /// <summary>单层签名（参数按键名序，值原样；state 受控词）。</summary>
    /// <param name="layer">已解析层。</param>
    public static string OfLayer(ResolvedLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var parameters = string.Join(
            ",",
            layer.Params.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value));

        return string.Concat(
            layer.Primitive,
            "|",
            layer.Z.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "|",
            parameters,
            "|",
            CapabilityStates.Format(layer.State));
    }

    /// <summary>层栈签名（按 z、role 排序后哈希；空栈 ⇒ 空串）。</summary>
    /// <param name="layers">层栈。</param>
    public static string OfLayers(IEnumerable<ResolvedLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var ordered = layers
            .OrderBy(layer => layer.Z)
            .ThenBy(layer => layer.Role, StringComparer.Ordinal)
            .Select(OfLayer)
            .ToArray();

        return ordered.Length == 0 ? string.Empty : Sha256(string.Join("\n", ordered));
    }

    /// <summary>材质签名（层栈签名）。</summary>
    /// <param name="resolution">层解析结果。</param>
    public static string OfMaterial(LayerResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return OfLayers(resolution.Layers);
    }

    /// <summary>容器签名（view:key|role|slot|write|material 签名按角色序）。</summary>
    /// <param name="id">容器全局键。</param>
    /// <param name="role">角色。</param>
    /// <param name="slot">结构槽。</param>
    /// <param name="write">写模式。</param>
    /// <param name="materialSignatures">材质槽 → 签名。</param>
    public static string OfContainer(
        string id,
        string role,
        string slot,
        string write,
        IReadOnlyDictionary<string, string>? materialSignatures)
    {
        var materials = materialSignatures is null
            ? string.Empty
            : string.Join(
                ",",
                materialSignatures.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Key + "=" + pair.Value));

        return $"{id}|{role}|{slot}|{write}|{materials}";
    }

    /// <summary>SHA256 小写十六进制（签名/指纹单源）。</summary>
    /// <param name="text">原文。</param>
    public static string Sha256(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
