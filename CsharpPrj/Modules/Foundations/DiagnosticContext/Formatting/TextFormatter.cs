
using System.Text;
using Modules.DiagnosticContext.Core;

namespace Modules.DiagnosticContext.Formatting;

public class TextFormatter : IContextFormatter
{
    public static readonly TextFormatter Default = new();

    public string Format(ScopeNode scope)
    {
        var sb = new StringBuilder();
        FormatNode(sb, scope, 0);
        return sb.ToString();
    }

    private static void FormatNode(StringBuilder sb, ScopeNode node, int depth)
    {
        var indent = new string(' ', depth * 2);

        if (depth == 0)
        {
            sb.AppendLine(node.Name);
        }
        else
        {
            sb.AppendLine($"{indent}── Scope: {node.Name} ──");
        }

        if (node.Events.Count > 0)
        {
            foreach (var evt in node.Events)
            {
                var dataJson = evt.GetDataJson();
                var dataPart = dataJson != null ? $"  {dataJson}" : "";
                sb.AppendLine($"{indent}  {evt.EventName}{dataPart}");
            }
        }

        if (node.State.Count > 0)
        {
            foreach (var kvp in node.State)
            {
                sb.AppendLine($"{indent}  {kvp.Key} = {kvp.Value ?? "(null)"}");
            }
        }

        if (node.Children.Count > 0)
        {
            foreach (var child in node.Children)
            {
                FormatNode(sb, child, depth + 1);
            }
        }
    }

}
