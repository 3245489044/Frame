using Modules.Node.Diagnostics;
using Modules.Node.Spec;

namespace Modules.Node.Projection;

/// <summary>
/// 节点记录校验（<b>记录级</b>，与声明级 <see cref="NodeSpecValidator"/> 分工）：
/// Snapshot 必带地址、Live 禁带地址、地址域须与读域一致。
/// <para>只产违规清单，不做修正；缺口一律可见化（N9）。</para>
/// </summary>
public static class NodeRecordValidator
{
    /// <summary>校验单条记录；<paramref name="expectedDomain"/> 给出时校验地址域一致性。</summary>
    public static NodeSpecValidationReport Validate(NodeProjectionItem item, string? expectedDomain = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var violations = new List<NodeSpecViolation>();

        if (item.Origin == ProjectionOrigin.Snapshot &&
            (item.Address is null || string.IsNullOrWhiteSpace(item.Address.NodeKey)))
        {
            violations.Add(new NodeSpecViolation(
                "spec.origin-address-missing",
                NodeDiagnosticSeverity.Error,
                "Snapshot 记录缺节点地址（逻辑键为空）：身份不完整",
                item.Key));
        }

        if (item.Origin == ProjectionOrigin.Live && item.Address is not null)
        {
            violations.Add(new NodeSpecViolation(
                "spec.origin-live-address-forbidden",
                NodeDiagnosticSeverity.Error,
                "Live 记录不得携带地址（Live 不对应任何已物化地址）",
                item.Key));
        }

        if (item.Address is { } address &&
            !string.IsNullOrWhiteSpace(expectedDomain) &&
            !string.IsNullOrWhiteSpace(address.DomainKey) &&
            !string.Equals(address.DomainKey, expectedDomain, StringComparison.Ordinal))
        {
            violations.Add(new NodeSpecViolation(
                "spec.address-domain-mismatch",
                NodeDiagnosticSeverity.Error,
                $"地址域 '{address.DomainKey}' 与读域 '{expectedDomain}' 不一致",
                item.Key));
        }

        return new NodeSpecValidationReport(violations);
    }
}
