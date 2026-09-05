using CodePathExplorer.Models;

namespace CodePathExplorer.Export;

public static class FormatHelper
{
    public static string FormatChainNode(CallChainNode node)
    {
        if (node.IsLoop)
            return $"[循环] {node.MethodName}";
        if (node.IsBranchPoint)
            return $"{node.MethodName} ({node.BranchCondition})";
        return node.MethodName;
    }

    public static string FormatChainPath(CallChain chain)
    {
        return string.Join(" → ", chain.Nodes.Select(FormatChainNode));
    }

    public static string GetOperationSymbol(FieldOperationType op) => op switch
    {
        FieldOperationType.Declaration => "声明",
        FieldOperationType.Read => "读",
        FieldOperationType.Write => "写",
        FieldOperationType.PassAsArgument => "传参",
        _ => "?"
    };

    public static string FormatFieldFlowNode(FieldFlowNode node)
    {
        var op = GetOperationSymbol(node.OperationType);
        var extra = node.PassedToMethod != null ? $" → {node.PassedToMethod}" : "";
        return $"[{op}] {node.MethodName}{extra}";
    }

    public static string FormatFieldFlowPath(FieldFlow flow)
    {
        return string.Join(" → ", flow.Nodes.Select(FormatFieldFlowNode));
    }

    public static string GetFieldSetStatus(FieldFlowSet fieldSet)
    {
        if (fieldSet.IsUnused) return " ⚠ 冗余";
        if (fieldSet.IsSingleMethodUse) return " ⚡ 可内化";
        return "";
    }

    public static string GetSuggestionIcon(SuggestionType type) => type switch
    {
        SuggestionType.RedundantField => "⚠",
        SuggestionType.InternalizableField => "⚡",
        SuggestionType.RedundantMethod => "⚠",
        SuggestionType.Simplifiable => "💡",
        _ => "•"
    };

    public static string GetSuggestionMarkdownIcon(SuggestionType type) => type switch
    {
        SuggestionType.RedundantField => "⚠️",
        SuggestionType.InternalizableField => "⚡",
        SuggestionType.RedundantMethod => "⚠️",
        SuggestionType.Simplifiable => "💡",
        _ => "•"
    };
}
