namespace MathematicalAlgorithms
{
    public class VariableUsageInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int DeclarationLine { get; set; }
        public int FirstUsedLine { get; set; }
        public int LastUsedLine { get; set; }
        public int UsageCount { get; set; }
        public HashSet<int> UsedLines { get; set; } = new();
        public bool IsParameter { get; set; }
        public bool IsLocal { get; set; }
        public bool IsField { get; set; }
        public double ScopeSpan => LastUsedLine - FirstUsedLine;
        public double ReuseFrequency => UsageCount > 1 ? (double)UsageCount / Math.Max(1, ScopeSpan) : 0;
    }
}
