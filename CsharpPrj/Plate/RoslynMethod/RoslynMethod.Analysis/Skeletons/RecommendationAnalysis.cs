using System.Collections.Generic;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    internal class RecommendationAnalysis
    {
        public Dictionary<ScoreCategory, double> Scores { get; set; } = new Dictionary<ScoreCategory, double>();
        public (ScoreCategory Category, double Score) DominantScore { get; set; }
        public RecommendationType RecommendationType { get; set; }
        public double Confidence { get; set; }
        public Dictionary<string, object> AnalysisMetrics { get; set; } = new Dictionary<string, object>();
    }
}
