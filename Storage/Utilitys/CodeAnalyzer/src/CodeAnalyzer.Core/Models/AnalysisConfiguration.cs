namespace CodeAnalyzer.Core.Models;

public class AnalysisConfiguration
{
    public double DistanceThreshold { get; set; } = 5.0;
    public int MaxClusters { get; set; } = 10;
    public double TypeDifferencePenalty { get; set; } = 2.0;
    public double PositionDistanceWeight { get; set; } = 0.01;
    public double ConfidenceThreshold { get; set; } = CodeAnalysisConstants.CONFIDENCE_MEDIUM;
}

public class CodeCluster
{
    public int Id { get; }
    public string Name { get; set; }
    public List<EnhancedCodeElement> Elements { get; }
    public double Cohesion { get; set; }
    public double Coupling { get; set; }

    public CodeCluster(int id)
    {
        Id = id;
        Elements = new List<EnhancedCodeElement>();
        Name = $"Cluster_{id}";
    }

    public void AddElement(EnhancedCodeElement element) => Elements.Add(element);
    public bool ContainsElement(string elementId) => Elements.Any(e => e.Id == elementId);
}

public class AnalysisResult
{
    public List<EnhancedCodeElement> Elements { get; set; } = new();
    public List<ElementDistance> Distances { get; set; } = new();
    public List<CodeCluster> Clusters { get; set; } = new();
    public List<RefactoringSuggestion> Suggestions { get; set; } = new();
    public Dictionary<string, object> Metrics { get; set; } = new();
    public double AnalysisTime { get; set; }
    public string Error { get; set; } = "";

    public static AnalysisResult Empty(string message = "No elements found") => new() { Error = message };
    public static AnalysisResult WithError(string error) => new() { Error = error };
}

public class RefactoringSuggestion
{
    public string Id { get; }
    public RefactoringType Type { get; }
    public double Confidence { get; set; }
    public string TargetLocation { get; set; } = "";
    public List<string> AffectedElements { get; set; } = new();
    public string Category { get; set; } = "";
    public string DetailedDescription { get; set; } = "";
    public string OptimizationType { get; set; } = "";
    public List<string> ElementNames { get; set; } = new();
    public List<VariableOptimizationDetail> VariableDetails { get; set; } = new();
    public List<string> Risks { get; set; } = new();

    public RefactoringSuggestion(string id, RefactoringType type)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Type = type;
    }
}

public class VariableOptimizationDetail
{
    public string VariableName { get; set; } = "";
    public int UsageCount { get; set; }
    public bool IsParameter { get; set; }
    public string Type { get; set; } = "";
}

public class RecommendedPrompt
{
    public string Prompt { get; set; } = "";
    public double Confidence { get; set; }
    public string Reason { get; set; } = "";
    public Dictionary<string, object> AnalysisDetails { get; set; } = new();
}

internal class RecommendationAnalysis
{
    public Dictionary<ScoreCategory, double> Scores { get; set; } = new();
    public (ScoreCategory Category, double Score) DominantScore { get; set; }
    public RecommendationType RecommendationType { get; set; }
    public double Confidence { get; set; }
    public Dictionary<string, object> AnalysisMetrics { get; set; } = new();
}

public class RecommendationResult
{
    public RecommendationType Type { get; set; }
    public double Confidence { get; set; }
    public ScoreCategory DominantCategory { get; set; }
    public double DominantScore { get; set; }
    public Dictionary<ScoreCategory, double> SupportingScores { get; set; } = new();
}
