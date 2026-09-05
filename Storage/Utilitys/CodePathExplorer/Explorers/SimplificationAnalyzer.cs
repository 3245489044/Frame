using CodePathExplorer.Models;

namespace CodePathExplorer.Explorers;

public class SimplificationAnalyzer
{
    public List<SimplificationSuggestion> Analyze(AnalysisResult result)
    {
        var suggestions = new List<SimplificationSuggestion>();

        AnalyzeRedundantFields(result, suggestions);

        AnalyzeInternalizableFields(result, suggestions);

        AnalyzeRedundantMethods(result, suggestions);

        return suggestions;
    }

    private void AnalyzeRedundantFields(AnalysisResult result, List<SimplificationSuggestion> suggestions)
    {
        foreach (var fieldSet in result.FieldFlowSets)
        {
            if (fieldSet.IsUnused)
            {
                suggestions.Add(new SimplificationSuggestion
                {
                    Type = SuggestionType.RedundantField,
                    Target = fieldSet.FieldName,
                    ClassName = fieldSet.ClassName,
                    Description = $"字段 '{fieldSet.FieldName}' 未被任何方法使用",
                    Detail = $"类型: {fieldSet.FieldType}, 类: {fieldSet.ClassName}"
                });
            }
        }
    }

    private void AnalyzeInternalizableFields(AnalysisResult result, List<SimplificationSuggestion> suggestions)
    {
        foreach (var fieldSet in result.FieldFlowSets)
        {
            if (!fieldSet.IsUnused && fieldSet.IsSingleMethodUse)
            {
                var usedMethod = fieldSet.Flows
                    .SelectMany(f => f.Nodes)
                    .FirstOrDefault(n => n.OperationType != FieldOperationType.Declaration)
                    ?.MethodName ?? "?";

                suggestions.Add(new SimplificationSuggestion
                {
                    Type = SuggestionType.InternalizableField,
                    Target = fieldSet.FieldName,
                    ClassName = fieldSet.ClassName,
                    Description = $"字段 '{fieldSet.FieldName}' 仅被 '{usedMethod}' 使用，可内化为局部变量",
                    Detail = $"类型: {fieldSet.FieldType}, 类: {fieldSet.ClassName}, 使用方法: {usedMethod}"
                });
            }
        }
    }

    private void AnalyzeRedundantMethods(AnalysisResult result, List<SimplificationSuggestion> suggestions)
    {
        var calledMethods = new HashSet<string>();
        foreach (var chain in result.CallChains)
        {
            foreach (var node in chain.Nodes)
            {
                calledMethods.Add(node.MethodFullName);
            }
        }

        var entryMethods = new HashSet<string>();
        foreach (var chain in result.CallChains)
        {
            entryMethods.Add(chain.EntryMethod);
        }

        var allCalledMethods = new HashSet<string>();
        foreach (var chain in result.CallChains)
        {
            foreach (var node in chain.Nodes)
            {
                if (!entryMethods.Contains(node.MethodFullName))
                {
                    allCalledMethods.Add(node.MethodFullName);
                }
            }
        }

        var orphanEntries = new HashSet<string>();
        foreach (var entry in entryMethods)
        {
            if (!allCalledMethods.Contains(entry))
            {
                orphanEntries.Add(entry);
            }
        }

        foreach (var orphan in orphanEntries)
        {
            var methodName = orphan.Contains('.') ? orphan[(orphan.LastIndexOf('.') + 1)..] : orphan;

            if (methodName is "Main" or "OnStartup" or "InitializeComponent" or "Dispose")
                continue;

            suggestions.Add(new SimplificationSuggestion
            {
                Type = SuggestionType.RedundantMethod,
                Target = orphan,
                ClassName = orphan.Contains('.') ? orphan[..orphan.LastIndexOf('.')] : "?",
                Description = $"方法 '{methodName}' 未被其他方法调用（孤立入口）",
                Detail = $"完整名称: {orphan}"
            });
        }
    }
}
