using System.Security.Cryptography;
using System.Text;
using DataCenter.Protocol.Dataflow;

namespace DataCenter.Core.Dataflow;

/// <summary>
/// 声明图内容指纹（FW4 使能件）：声明漂移 ⇒ 版本必变 ⇒ 读模型必刷新。
/// <para>
/// 纪律：① 纯函数（同声明两次计算一致，无时间/随机/IO）；② 只读声明面字段（产品/层/边），
/// 与声明顺序无关（内部排序）；③ 字段以「长度:值」编码，避免分隔符碰撞；
/// ④ 不进入投影产物（仅作版本源与漂移门判据）。
/// </para>
/// </summary>
public static class DeclaredGraphFingerprint
{
    /// <summary>声明图指纹（SHA-256 十六进制小写）。</summary>
    public static string Compute(DeclaredGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var payload = Canonicalize(graph);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>声明图版本值（指纹前 8 字节 → 63 位非负整数；供 <c>ReadModelVersionMode.Declared</c> 探针使用）。</summary>
    public static long VersionOf(DeclaredGraph graph)
    {
        var fingerprint = Compute(graph);
        var bytes = Convert.FromHexString(fingerprint);
        var value = BitConverter.ToUInt64(bytes, 0) & 0x7FFF_FFFF_FFFF_FFFFUL;
        return (long)value;
    }

    /// <summary>规范化文本（声明顺序无关；产品/层/边各自排序）。</summary>
    public static string Canonicalize(DeclaredGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new StringBuilder();
        foreach (var product in (graph.Products ?? Array.Empty<ProductDeclaration>())
                     .Where(p => p is not null)
                     .OrderBy(p => p.ProductId, StringComparer.Ordinal))
        {
            Append(builder, "p");
            Append(builder, product.ProductId);
            Append(builder, product.LevelId);
            Append(builder, product.Role);
            Append(builder, product.Version.ToString());
            Append(builder, product.Stage);
            Append(builder, product.Compatibility);
            Append(builder, product.IdempotencyKey);
            AppendFields(builder, product.InFields);
            AppendFields(builder, product.OutFields);
            foreach (var dependency in (product.DependsOn ?? Array.Empty<string>())
                         .OrderBy(d => d, StringComparer.Ordinal))
            {
                Append(builder, dependency);
            }
        }

        foreach (var level in (graph.Levels ?? Array.Empty<LevelDeclaration>())
                     .Where(l => l is not null)
                     .OrderBy(l => l.LevelId, StringComparer.Ordinal))
        {
            Append(builder, "l");
            Append(builder, level.LevelId);
            Append(builder, level.EntityType);
            Append(builder, level.ContainsEdgeFamily);
            Append(builder, level.ProcessId);
        }

        foreach (var edge in (graph.Edges ?? Array.Empty<EdgeDeclaration>())
                     .Where(e => e is not null)
                     .OrderBy(EdgeSortKey, StringComparer.Ordinal))
        {
            Append(builder, "e");
            Append(builder, edge.FromProductId);
            Append(builder, edge.EdgeKind);
            Append(builder, edge.ToProductId);
            Append(builder, edge.Label);
            Append(builder, edge.Assertion.ToString());
            AppendValues(builder, edge.FromFields);
            AppendValues(builder, edge.ToFields);
            if (edge.Properties is { Count: > 0 } properties)
            {
                foreach (var key in properties.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    Append(builder, key);
                    Append(builder, properties[key]);
                }
            }
        }

        return builder.ToString();
    }

    private static string EdgeSortKey(EdgeDeclaration edge)
        => string.Join('\u001F', edge.FromProductId, edge.EdgeKind, edge.ToProductId, edge.Label ?? string.Empty);

    private static void AppendFields(StringBuilder builder, IReadOnlyList<FieldSpec>? fields)
    {
        foreach (var field in (fields ?? Array.Empty<FieldSpec>()).Where(f => f is not null))
        {
            Append(builder, field.Name);
            Append(builder, field.Type);
            Append(builder, field.Required ? "1" : "0");
        }

        Append(builder, ".");
    }

    private static void AppendValues(StringBuilder builder, IReadOnlyList<string>? values)
    {
        foreach (var value in values ?? Array.Empty<string>())
        {
            Append(builder, value);
        }

        Append(builder, ".");
    }

    /// <summary>长度前缀编码（避免字段内容含分隔符时碰撞）。</summary>
    private static void Append(StringBuilder builder, string? value)
    {
        var text = value ?? string.Empty;
        builder.Append(text.Length).Append(':').Append(text);
    }
}
