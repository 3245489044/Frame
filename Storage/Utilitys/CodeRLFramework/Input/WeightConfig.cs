namespace CodeRLFramework.Input
{
    public class WeightConfig
    {
        public double CodebaseWeight { get; set; } = 0.7;

        public double TemplateWeight { get; set; } = 0.3;

        public double ExplorationRate { get; set; } = 0.1;

        public double Temperature { get; set; } = 1.0;

        public double ConfidenceThreshold { get; set; } = 0.3;

        public double GraphStructureWeight { get; set; } = 0.20;

        public double NavigationQualityWeight { get; set; } = 0.15;

        public double BaseRewardWeight { get; set; } = 0.40;

        public double PlaceholderIntegrityWeight { get; set; } = 0.10;

        public bool NormalizeWeights { get; set; } = true;

        public (double normalizedAlpha, double normalizedBeta) GetNormalizedWeights()
        {
            if (!NormalizeWeights)
                return (CodebaseWeight, TemplateWeight);

            double sum = CodebaseWeight + TemplateWeight;
            if (sum <= 0) return (0.5, 0.5);
            return (CodebaseWeight / sum, TemplateWeight / sum);
        }

        public bool IsValid()
        {
            return CodebaseWeight >= 0 && CodebaseWeight <= 1
                && TemplateWeight >= 0 && TemplateWeight <= 1
                && ExplorationRate >= 0 && ExplorationRate <= 1
                && Temperature > 0;
        }
    }
}
