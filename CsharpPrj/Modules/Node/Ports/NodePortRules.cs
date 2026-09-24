using Modules.Node.Diagnostics;

namespace Modules.Node.Ports;

/// <summary>端口准入角色（规则单源；值由调用侧按端口名自行取出，规则不含值语义）。</summary>
public enum PortAdmission
{
    /// <summary>声明端口命中且有值。</summary>
    DeclaredHit,

    /// <summary>声明端口未命中或值为 null（缺口可见化，不静默跳过）。</summary>
    DeclaredMissing,

    /// <summary>未声明 ResultKeys → 由结果结构推断（必产 <c>port.inferred</c> 诊断）。</summary>
    Inferred,

    /// <summary>无命名端口：结果整存于默认端口（端口名 = 节点键）。</summary>
    Default,
}

/// <summary>结果成员面（值形态无关：JSON 对象属性 / CLR 公共属性 / 字典键）。</summary>
public sealed record PortMember(string Name, bool HasValue);

/// <summary>单个端口准入判定（声明命中/缺失携带声明名，推断携带成员名）。</summary>
public sealed record PortDecision(string Port, PortAdmission Admission);

public sealed record PortRuleResult(
    IReadOnlyList<PortDecision> Decisions,
    bool UsedInference,
    IReadOnlyList<NodeDiagnostic> Diagnostics);

/// <summary>
/// 端口准入规则单源（G1/G2 的"规则"半边；"值"半边由各前端提供——JSON 文本路径与宿主原生值路径共用本规则）：
/// <list type="number">
/// <item>声明端口定向命中（<c>ResultKeys</c>）；</item>
/// <item>未声明 → 成员名兜底，且必产 <c>port.inferred</c> 诊断（治 P12：禁静默猜端口）；</item>
/// <item>非对象结果（简单值/不可枚举成员）→ 默认端口 = 节点键整存（<c>port.default</c>）；</item>
/// <item>null 值/端口不产槽位，声明缺失产 <c>port.declared-missing</c> 可见化（治 P15）。</item>
/// </list>
/// 不解析键映射、不做类型转换、不决定读哪些位置。
/// </summary>
public static class NodePortRules
{
    /// <summary>
    /// 准入判定。<paramref name="members"/> = <c>null</c> 即"非对象结果"（默认端口整存）；
    /// <paramref name="prefixDiagnostics"/> 供前端先置诊断（如 JSON 解析兜底）以保持既有诊断顺序。
    /// </summary>
    public static PortRuleResult Decide(
        string nodeKey,
        IReadOnlyList<PortMember>? members,
        IReadOnlyList<string>? declaredPorts = null,
        IReadOnlyList<NodeDiagnostic>? prefixDiagnostics = null,
        StringComparer? memberComparer = null)
    {
        var declared = declaredPorts ?? Array.Empty<string>();
        var diagnostics = new List<NodeDiagnostic>();

        if (prefixDiagnostics is not null)
        {
            diagnostics.AddRange(prefixDiagnostics);
        }

        if (members is null)
        {
            diagnostics.Add(new NodeDiagnostic(
                "port.default",
                NodeDiagnosticSeverity.Info,
                "无命名端口：结果整存于默认端口（端口名 = 节点键）",
                nodeKey));

            return new PortRuleResult(
                new[] { new PortDecision(nodeKey, PortAdmission.Default) },
                UsedInference: declared.Count == 0,
                diagnostics);
        }

        var decisions = new List<PortDecision>();

        if (declared.Count > 0)
        {
            var comparer = memberComparer ?? StringComparer.Ordinal;

            foreach (var port in declared)
            {
                if (HasValue(members, port, comparer))
                {
                    decisions.Add(new PortDecision(port, PortAdmission.DeclaredHit));
                    continue;
                }

                decisions.Add(new PortDecision(port, PortAdmission.DeclaredMissing));
                diagnostics.Add(new NodeDiagnostic(
                    "port.declared-missing",
                    NodeDiagnosticSeverity.Info,
                    $"声明端口 '{port}' 在结果中未命中或为 null（缺口可见化，不静默跳过）",
                    nodeKey));
            }

            return new PortRuleResult(decisions, UsedInference: false, diagnostics);
        }

        foreach (var member in members)
        {
            if (!member.HasValue)
            {
                continue;
            }

            decisions.Add(new PortDecision(member.Name, PortAdmission.Inferred));
            diagnostics.Add(new NodeDiagnostic(
                "port.inferred",
                NodeDiagnosticSeverity.Warning,
                $"未声明 ResultKeys：端口 '{member.Name}' 由结果结构推断（跨模块复用请显式声明）",
                nodeKey));
        }

        return new PortRuleResult(decisions, UsedInference: true, diagnostics);
    }

    private static bool HasValue(IReadOnlyList<PortMember> members, string name, StringComparer comparer)
    {
        foreach (var member in members)
        {
            if (comparer.Equals(member.Name, name))
            {
                return member.HasValue;
            }
        }

        return false;
    }
}
