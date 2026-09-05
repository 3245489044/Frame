namespace CodePathExplorer.Models;

public class AnalysisResult
{
    public string SourcePath { get; set; } = "";

    public int FileCount { get; set; }

    public int ClassCount { get; set; }

    public List<CallChain> CallChains { get; set; } = new();

    public List<FieldFlowSet> FieldFlowSets { get; set; } = new();

    public List<SimplificationSuggestion> Suggestions { get; set; } = new();
}

public class SimplificationSuggestion
{
    public SuggestionType Type { get; set; }

    public string Description { get; set; } = "";

    public string Target { get; set; } = "";

    public string ClassName { get; set; } = "";

    public string Detail { get; set; } = "";
}

public enum SuggestionType
{
    RedundantField,
    InternalizableField,
    RedundantMethod,
    Simplifiable
}
