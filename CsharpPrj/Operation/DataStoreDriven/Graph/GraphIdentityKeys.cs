using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UniversalWorkflowFramework.Core.Models;

namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 图库内容身份 / 墓碑键单源：所有指纹与墓碑键只经本类构造（禁各处手写拼串）。
/// <para>规范串 = 长度前缀 + <c>:</c> + 值 + <c>U+001F</c>，消除字段边界歧义；摘要 = SHA-256 小写十六进制。</para>
/// </summary>
public static class GraphIdentityKeys
{
    private const char Separator = '\u001F';

    /// <summary>规范串（长度前缀 + U+001F 分隔；null 视为空串）。</summary>
    public static string Canonical(params string?[] parts)
    {
        var sb = new StringBuilder();
        if (parts is null) return sb.ToString();
        foreach (var part in parts)
        {
            var value = part ?? string.Empty;
            sb.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(Separator);
        }
        return sb.ToString();
    }

    /// <summary>SHA-256 小写十六进制。</summary>
    public static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty))).ToLowerInvariant();

    /// <summary>声明内容指纹（WorkflowId + 两端类型/Id/端口 + RefType + KeyMappingRef）。</summary>
    public static string DeclarationFingerprint(RelationDoc doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return Sha256(Canonical(
            doc.WorkflowId,
            doc.SourceType,
            doc.Source,
            doc.SourcePort,
            doc.TargetType,
            doc.Target,
            doc.TargetPort,
            doc.RefType,
            doc.KeyMappingRef));
    }

    /// <summary>声明墓碑键（<c>decl:{指纹}</c>）。</summary>
    public static string DeclarationTombstoneKey(RelationDoc doc)
        => DeclarationTombstoneKey(DeclarationFingerprint(doc));

    /// <summary>声明墓碑键（由指纹构造）。</summary>
    public static string DeclarationTombstoneKey(string fingerprint)
        => "decl:" + (fingerprint ?? string.Empty);

    /// <summary>边墓碑键（<c>edge:{指纹}</c>；指纹含端点键与边类型）。</summary>
    public static string EdgeTombstoneKey(string sourceKey, string targetKey, string refType)
        => "edge:" + Sha256(Canonical(sourceKey, targetKey, refType));

    /// <summary>端点墓碑键（可读键 <c>endpoint:{type}:{id}</c>；端点键文法保证二义性为零）。</summary>
    public static string EndpointTombstoneKey(string resourceType, string id)
        => "endpoint:" + (resourceType ?? string.Empty) + ":" + (id ?? string.Empty);

    /// <summary>端点墓碑键（标识重载）。</summary>
    public static string EndpointTombstoneKey(ResourceIdentifier id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return EndpointTombstoneKey(id.ResourceType ?? string.Empty, id.Id ?? string.Empty);
    }
}
