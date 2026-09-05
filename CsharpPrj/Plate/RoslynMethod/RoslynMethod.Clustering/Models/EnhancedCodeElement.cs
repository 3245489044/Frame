#nullable disable

namespace MathematicalAlgorithms
{
    public class EnhancedCodeElement
    {
        public string Id { get; }
        public string Name { get; }
        public string FullName { get; }
        public string Namespace { get; }
        public ElementType Type { get; }
        public int LineNumber { get; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string ContainingType { get; }
        public AccessibilityLevel Accessibility { get; set; }
        public Dictionary<string, int> References { get; } = new();
        public HashSet<string> ReferencedBy { get; } = new();
        public int ReferenceCount => References.Values.Sum();
        public List<string> Parameters { get; } = new();
        public string ReturnType { get; set; } = string.Empty;
        public double ComplexityScore { get; set; }
        public bool IsPure { get; set; }
        public bool HasSideEffects { get; set; }
        public bool IsStatic { get; set; }
        public bool IsVirtual { get; set; }
        public bool IsAbstract { get; set; }
        public bool HasOverloads { get; set; }
        public bool IsOverride { get; set; }
        public int LinesOfCode { get; set; }
        public int CommentLines { get; set; }
        public double CommentDensity => LinesOfCode > 0 ? (double)CommentLines / LinesOfCode : 0;
        public List<VariableUsageInfo> Variables { get; } = new();
        public HashSet<string> UsedTypes { get; } = new();
        public Dictionary<string, int> TypeUsageFrequency { get; } = new();
        public int VariableCount => Variables.Count;
        public int ParameterCount { get; set; }
        public int BodyLines { get; set; }
        public double VariableReuseRatio { get; set; }
        public double VariableScopeComplexity { get; set; }


        public EnhancedCodeElement(string id, string name, ElementType type, int line, string container)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Type = type;
            LineNumber = line;
            StartLine = line;
            EndLine = line;
            ContainingType = container ?? string.Empty;
        }

        public double GetReferenceStrength(string elementName)
        {
            if (ReferenceCount == 0 || !References.TryGetValue(elementName, out int count))
                return 0;

            return count / Math.Max(1.0, (double)ReferenceCount);
        }
    }
}
