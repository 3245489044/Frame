using Modules.Node.Diagnostics;
using Modules.Node.Identity;

namespace Modules.Node.Instances;

/// <summary>终答归因规则（声明驱动）：<c>LastProduced</c> = 逆到达序最后一个；<c>Declared</c> = 显式声明。</summary>
public enum NodeTerminalRule
{
    /// <summary>最后产出（按到达序的最后一个子节点）。</summary>
    LastProduced,

    /// <summary>显式声明终答节点。</summary>
    Declared,
}

/// <summary>
/// 轮产物装配（终答归因 + 子引用去重 + 归档）：<b>只存子引用</b>（R8），不复制子载荷。
/// <para>缺终答 ⇒ 诊断 <c>artifact.terminal-missing</c>（不伪造终答）。</para>
/// </summary>
public sealed class NodeArtifactAssembler
{
    private readonly NodeTerminalRule _rule;
    private readonly INodeDiagnosticsSink _diagnostics;

    /// <param name="rule">终答归因规则（缺省 = 最后产出）。</param>
    /// <param name="diagnostics">诊断出口。</param>
    public NodeArtifactAssembler(
        NodeTerminalRule rule = NodeTerminalRule.LastProduced,
        INodeDiagnosticsSink? diagnostics = null)
    {
        _rule = rule;
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    /// <summary>装配轮产物（子引用去重保序；终答按规则归因）。</summary>
    /// <param name="containerKey">容器键（非空）。</param>
    /// <param name="roundKey">轮键（非空）。</param>
    /// <param name="arrivalOrderedChildren">子节点地址（到达序）。</param>
    /// <param name="declaredTerminal">显式声明的终答（<see cref="NodeTerminalRule.Declared"/> 用）。</param>
    /// <param name="terminalText">终答文本。</param>
    /// <param name="archives">归档条目。</param>
    public NodeArtifact Assemble(
        string containerKey,
        string roundKey,
        IReadOnlyList<NodeAddress> arrivalOrderedChildren,
        NodeAddress? declaredTerminal = null,
        string? terminalText = null,
        IReadOnlyList<NodeArtifactArchive>? archives = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(roundKey);
        ArgumentNullException.ThrowIfNull(arrivalOrderedChildren);

        var children = new List<NodeAddress>(arrivalOrderedChildren.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in arrivalOrderedChildren)
        {
            if (child is null || string.IsNullOrWhiteSpace(child.NodeKey))
            {
                continue;
            }

            if (seen.Add(child.EndpointId))
            {
                children.Add(child);
            }
        }

        var terminal = _rule == NodeTerminalRule.Declared ? declaredTerminal : children.LastOrDefault();
        if (_rule == NodeTerminalRule.Declared && terminal is not null && !seen.Contains(terminal.EndpointId))
        {
            _diagnostics.Report(new NodeDiagnostic(
                "artifact.terminal-missing",
                NodeDiagnosticSeverity.Warning,
                $"声明终答 '{terminal.EndpointId}' 不在本轮子引用内：终答置空（不伪造）",
                roundKey));
            terminal = null;
        }

        if (terminal is null)
        {
            _diagnostics.Report(new NodeDiagnostic(
                "artifact.terminal-missing",
                NodeDiagnosticSeverity.Warning,
                "本轮无可归因终答：Terminal 置空（不伪造终答）",
                roundKey));
        }

        return new NodeArtifact(
            containerKey,
            roundKey,
            children,
            terminal,
            terminal is null ? null : terminalText,
            archives);
    }
}
