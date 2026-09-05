using System.Text;
using CodePathExplorer.Models;

namespace CodePathExplorer.Export;

public class ChainPrinter
{
    public string PrintReport(AnalysisResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine("╔══════════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║                    CodePathExplorer 分析报告                        ║");
        sb.AppendLine($"║                    目标: {result.SourcePath,-40}║");
        sb.AppendLine($"║                    文件: {result.FileCount}个  类: {result.ClassCount}个              ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════════════╝");
        sb.AppendLine();

        PrintCallChains(sb, result.CallChains);
        PrintFieldFlows(sb, result.FieldFlowSets);
        PrintSuggestions(sb, result.Suggestions);

        return sb.ToString();
    }

    private void PrintCallChains(StringBuilder sb, List<CallChain> chains)
    {
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("  【函数调用链】直线型多链架构");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine();

        if (chains.Count == 0)
        {
            sb.AppendLine("  (无调用链)");
            sb.AppendLine();
            return;
        }

        var grouped = chains.GroupBy(c => c.EntryMethod);
        foreach (var group in grouped)
        {
            sb.AppendLine($"  [入口] {group.Key}");
            sb.AppendLine();

            foreach (var chain in group)
            {
                sb.Append($"    Chain {chain.ChainId}: ");
                for (int i = 0; i < chain.Nodes.Count; i++)
                {
                    var node = chain.Nodes[i];
                    if (i > 0) sb.Append(" → ");
                    sb.Append(FormatHelper.FormatChainNode(node));
                }
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        sb.AppendLine($"  共 {chains.Count} 条调用链");
        sb.AppendLine();
    }

    private void PrintFieldFlows(StringBuilder sb, List<FieldFlowSet> flowSets)
    {
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("  【字段数据流】直线型多链架构");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine();

        if (flowSets.Count == 0)
        {
            sb.AppendLine("  (无字段)");
            sb.AppendLine();
            return;
        }

        foreach (var fieldSet in flowSets)
        {
            var status = FormatHelper.GetFieldSetStatus(fieldSet);

            sb.AppendLine($"  Field: {fieldSet.ClassName}.{fieldSet.FieldName}  ({fieldSet.FieldType}){status}");

            foreach (var flow in fieldSet.Flows)
            {
                sb.Append($"    Flow {flow.FlowId}: ");
                sb.AppendLine(FormatHelper.FormatFieldFlowPath(flow));
            }
            sb.AppendLine();
        }
    }

    private void PrintSuggestions(StringBuilder sb, List<SimplificationSuggestion> suggestions)
    {
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("  【简化建议】");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine();

        if (suggestions.Count == 0)
        {
            sb.AppendLine("  (无简化建议)");
            sb.AppendLine();
            return;
        }

        foreach (var suggestion in suggestions)
        {
            var icon = FormatHelper.GetSuggestionIcon(suggestion.Type);

            sb.AppendLine($"  {icon} [{suggestion.Type}] {suggestion.Description}");
            sb.AppendLine($"      {suggestion.Detail}");
            sb.AppendLine();
        }
    }
}
