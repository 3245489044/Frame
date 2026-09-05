using System.Text;
using CodePathExplorer.Models;

namespace CodePathExplorer.Export;

public class MarkdownExporter
{
    public string ExportToMarkdown(AnalysisResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# CodePathExplorer 分析报告");
        sb.AppendLine();
        sb.AppendLine($"- **目标路径**: `{result.SourcePath}`");
        sb.AppendLine($"- **文件数**: {result.FileCount}");
        sb.AppendLine($"- **类数**: {result.ClassCount}");
        sb.AppendLine($"- **调用链数**: {result.CallChains.Count}");
        sb.AppendLine($"- **字段数据流数**: {result.FieldFlowSets.Count}");
        sb.AppendLine();

        ExportCallChainsMarkdown(sb, result.CallChains);
        ExportFieldFlowsMarkdown(sb, result.FieldFlowSets);
        ExportSuggestionsMarkdown(sb, result.Suggestions);

        return sb.ToString();
    }

    private void ExportCallChainsMarkdown(StringBuilder sb, List<CallChain> chains)
    {
        sb.AppendLine("## 函数调用链");
        sb.AppendLine();

        if (chains.Count == 0)
        {
            sb.AppendLine("*(无调用链)*");
            sb.AppendLine();
            return;
        }

        var grouped = chains.GroupBy(c => c.EntryMethod);
        foreach (var group in grouped)
        {
            sb.AppendLine($"### 入口: {group.Key}");
            sb.AppendLine();
            sb.AppendLine("| Chain ID | 调用路径 |");
            sb.AppendLine("|----------|----------|");

            foreach (var chain in group)
            {
                var path = FormatHelper.FormatChainPath(chain);
                sb.AppendLine($"| {chain.ChainId} | {path} |");
            }
            sb.AppendLine();
        }
    }

    private void ExportFieldFlowsMarkdown(StringBuilder sb, List<FieldFlowSet> flowSets)
    {
        sb.AppendLine("## 字段数据流");
        sb.AppendLine();

        if (flowSets.Count == 0)
        {
            sb.AppendLine("*(无字段)*");
            sb.AppendLine();
            return;
        }

        foreach (var fieldSet in flowSets)
        {
            var status = FormatHelper.GetFieldSetStatus(fieldSet);

            sb.AppendLine($"### {fieldSet.ClassName}.{fieldSet.FieldName} `{fieldSet.FieldType}`{status}");
            sb.AppendLine();

            sb.AppendLine("| Flow ID | 数据流路径 |");
            sb.AppendLine("|---------|------------|");

            foreach (var flow in fieldSet.Flows)
            {
                var path = FormatHelper.FormatFieldFlowPath(flow);
                sb.AppendLine($"| {flow.FlowId} | {path} |");
            }
            sb.AppendLine();
        }
    }

    private void ExportSuggestionsMarkdown(StringBuilder sb, List<SimplificationSuggestion> suggestions)
    {
        sb.AppendLine("## 简化建议");
        sb.AppendLine();

        if (suggestions.Count == 0)
        {
            sb.AppendLine("*(无简化建议)*");
            sb.AppendLine();
            return;
        }

        foreach (var suggestion in suggestions)
        {
            var icon = FormatHelper.GetSuggestionMarkdownIcon(suggestion.Type);

            sb.AppendLine($"- {icon} **{suggestion.Description}**");
            sb.AppendLine($"  - {suggestion.Detail}");
            sb.AppendLine();
        }
    }
}
