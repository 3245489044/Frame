using System.Text;

namespace Share.CircuitFramework.Analysis;

/// <summary>
/// 全流程图渲染器（R4）：DOT / Mermaid / JSON 三态输出（声明式、可 diff、可离线审阅）。
/// 纯函数（无状态、无时间戳）⇒ 对同一 <see cref="ControlFlowGraph"/> 的输出幂等。
/// </summary>
public static class CircuitFlowRenderer
{
    /// <summary>Graphviz DOT 输出。</summary>
    public static string ToDot(ControlFlowGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var sb = new StringBuilder();
        sb.AppendLine("digraph circuit {");
        foreach (var n in graph.Nodes)
            sb.Append("  \"").Append(Escape(n.Id)).Append("\" [label=\"")
              .Append(Escape(n.Id)).Append("\\n").Append(Escape(n.Kind)).Append("\"];").AppendLine();
        foreach (var e in graph.Edges)
        {
            sb.Append("  \"").Append(Escape(e.FromNodeId)).Append("\" -> \"").Append(Escape(e.ToNodeId)).Append('"');
            if (!string.IsNullOrEmpty(e.Label))
                sb.Append(" [label=\"").Append(Escape(e.Label!)).Append("\"]");
            sb.AppendLine(";");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>Mermaid flowchart 输出。</summary>
    public static string ToMermaid(ControlFlowGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        foreach (var n in graph.Nodes)
            sb.Append("  ").Append(Sanitize(n.Id)).Append("[\"").Append(Escape(n.Id)).Append("\"]").AppendLine();
        foreach (var e in graph.Edges)
        {
            sb.Append("  ").Append(Sanitize(e.FromNodeId)).Append(" --> ");
            if (!string.IsNullOrEmpty(e.Label))
                sb.Append("|\"").Append(Escape(e.Label!)).Append("\"| ");
            sb.Append(Sanitize(e.ToNodeId)).AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>JSON 输出（节点 + 边；确定性顺序 ⇒ 可 diff）。</summary>
    public static string ToJson(ControlFlowGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"nodes\": [");
        for (var i = 0; i < graph.Nodes.Count; i++)
        {
            var n = graph.Nodes[i];
            sb.Append("    {\"id\": \"").Append(Escape(n.Id))
              .Append("\", \"kind\": \"").Append(Escape(n.Kind))
              .Append("\", \"isRegion\": ").Append(n.IsRegion ? "true" : "false")
              .Append(", \"isCall\": ").Append(n.IsCall ? "true" : "false")
              .Append('}');
            if (i < graph.Nodes.Count - 1) sb.Append(',');
            sb.AppendLine();
        }
        sb.AppendLine("  ],");
        sb.AppendLine("  \"edges\": [");
        for (var i = 0; i < graph.Edges.Count; i++)
        {
            var e = graph.Edges[i];
            sb.Append("    {\"from\": \"").Append(Escape(e.FromNodeId))
              .Append("\", \"to\": \"").Append(Escape(e.ToNodeId))
              .Append("\", \"kind\": \"").Append(e.Kind).Append('"');
            if (!string.IsNullOrEmpty(e.Label))
                sb.Append(", \"label\": \"").Append(Escape(e.Label!)).Append('"');
            sb.Append('}');
            if (i < graph.Edges.Count - 1) sb.Append(',');
            sb.AppendLine();
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Sanitize(string id)
    {
        var sb = new StringBuilder(id.Length + 1);
        foreach (var ch in id)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, 'n');
        return sb.ToString();
    }
}
