
using System.Text;
using Modules.DiagnosticContext.Core;

namespace Modules.DiagnosticContext.Formatting;

public class SummaryFormatter : IContextFormatter
{
    public static readonly SummaryFormatter Default = new();

    public string Format(ScopeNode scope)
    {
        var sb = new StringBuilder();
        FormatSummary(sb, scope, 0);
        return sb.ToString();
    }

    private static void FormatSummary(StringBuilder sb, ScopeNode node, int depth)
    {
        var indent = new string(' ', depth * 2);
        var elapsedMs = node.Events.Count > 0
            ? node.Events.Last().RelativeMs
            : (long)(DateTime.UtcNow - node.CreatedAt).TotalMilliseconds;
        var uniqueEvents = node.Events.Select(e => e.EventName).Distinct().Count();
        var totalChildren = CountAllChildren(node);

        sb.AppendLine($"{indent}[Diagnostic Summary: {node.Name}]");
        sb.AppendLine($"{indent}  Duration: {elapsedMs / 1000.0:F3}s");
        sb.AppendLine($"{indent}  Events: {node.Events.Count} ({uniqueEvents} unique types)");
        sb.AppendLine($"{indent}  State: {node.State.Count} keys");
        sb.AppendLine($"{indent}  Scopes: {node.Children.Count} direct, {totalChildren} total");
        sb.AppendLine();

        foreach (var child in node.Children)
        {
            FormatSummary(sb, child, depth + 1);
        }
    }

    private static int CountAllChildren(ScopeNode node)
    {
        var count = node.Children.Count;
        foreach (var child in node.Children)
            count += CountAllChildren(child);
        return count;
    }

}
