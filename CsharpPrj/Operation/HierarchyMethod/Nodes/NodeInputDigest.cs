using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Nodes;

/// <summary>
/// 节点入参内容摘要（确定性、有界 16 hex）：同解析入参 ⇒ 同 digest ⇒ 可单飞复用。
/// 纪律：必须来自解析后的入参（ResolveInputs 结果），不得来自原始文本。
/// </summary>
public static class NodeInputDigest
{
    public static string? Compute(string workflowId, string nodeKey, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (string.IsNullOrWhiteSpace(workflowId) || string.IsNullOrWhiteSpace(nodeKey)) return null;
        try
        {
            var canonical = new SortedDictionary<string, string?>(StringComparer.Ordinal);
            if (parameters is not null)
            {
                foreach (var pair in parameters)
                {
                    if (pair.Key is null) continue;
                    canonical[pair.Key] = Canonical(pair.Value);
                }
            }

            var payload = workflowId + "\n" + nodeKey + "\n" + JsonSerializer.Serialize(canonical);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash)[..16];
        }
        catch
        {
            // 无法计算 ⇒ Unknown（调用方禁用本次复用，可见化，不猜）。
            return null;
        }
    }

    private static string? Canonical(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => JsonSerializer.Serialize(value),
    };
}

/// <summary>
/// 节点输出元数据写入口（可选能力；NodeOutputStore 实现）。
/// 与端口写共用同一业务节点载荷，不新增表/列/写门。
/// </summary>
public interface INodeOutputMetadataWriter
{
    Task WriteInputDigestAsync(string workflowId, string nodeKey, string inputDigest, CancellationToken ct = default);
}
