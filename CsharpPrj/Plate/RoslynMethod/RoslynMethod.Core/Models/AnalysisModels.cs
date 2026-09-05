using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;


namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    public class AnalysisContext : IDisposable
    {
        public SyntaxTree SyntaxTree { get; set; }
        public SemanticModel SemanticModel { get; set; }
        public Compilation Compilation { get; set; }
        public SyntaxNode Root { get; set; }
        public string SourceCode { get; set; }
        public string FilePath { get; set; }
        public CancellationToken CancellationToken { get; set; }

        public AnalysisContext() { }

        public AnalysisContext(string sourceCode, string filePath = null)
        {
            SourceCode = sourceCode;
            FilePath = filePath;
            SyntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            Compilation = CSharpCompilation.Create("Analysis")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(SyntaxTree);
            SemanticModel = Compilation.GetSemanticModel(SyntaxTree);
            Root = SyntaxTree.GetRoot();
        }

        public AnalysisContext(SyntaxTree syntaxTree, SemanticModel semanticModel, Compilation compilation)
        {
            SyntaxTree = syntaxTree;
            SemanticModel = semanticModel;
            Compilation = compilation;
            Root = syntaxTree.GetRoot();
        }

        public static AnalysisContext Create(string sourceCode)
        {
            return new AnalysisContext(sourceCode);
        }

        public SemanticModel GetSemanticModel(bool isCurrent = true)
        {
            return SemanticModel;
        }

        public bool HasCompilationErrors()
        {
            var diagnostics = Compilation.GetDiagnostics();
            return diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        }

        public void ValidateOrThrow()
        {
            if (HasCompilationErrors())
                throw new InvalidOperationException("代码存在编译错误");
        }

        public ImmutableArray<Diagnostic> GetDiagnostics()
        {
            return Compilation.GetDiagnostics();
        }

        public string GetSourceText()
        {
            return SourceCode ?? Root.ToFullString();
        }

        public void Dispose()
        {
        }
    }


    public class EnhancedCodeElement
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public ElementType Type { get; set; }
        public int LineNumber { get; set; }
        public string ContainingType { get; set; }
        public string ReturnType { get; set; }
        public string Accessibility { get; set; }
        public bool IsStatic { get; set; }
        public bool IsVirtual { get; set; }
        public bool IsAbstract { get; set; }
        public bool IsPure { get; set; }
        public bool HasSideEffects { get; set; }
        public bool HasOverloads { get; set; }
        public int ComplexityScore { get; set; }
        public double VariableReuseRatio { get; set; }
        public double VariableScopeComplexity { get; set; }
        public List<string> Parameters { get; set; } = new();
        public List<VariableUsageInfo> Variables { get; set; } = new();
        public Dictionary<string, int> References { get; set; } = new();
        public List<string> ReferencedBy { get; set; } = new();
        public HashSet<string> UsedTypes { get; set; } = new();
        public Dictionary<string, int> TypeUsageFrequency { get; set; } = new();

        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public double BodyLines { get; set; }
        public int ParameterCount { get; set; }
        public int VariableCount => Variables.Count;
        public int ReferenceCount => References.Values.Sum();
        public bool IsOverride { get; set; }
        public int LinesOfCode { get; set; }
        public int CommentLines { get; set; }

        public EnhancedCodeElement() { }

        public EnhancedCodeElement(string id, string name, ElementType type, int lineNumber, string containingType)
        {
            Id = id;
            Name = name;
            Type = type;
            LineNumber = lineNumber;
            ContainingType = containingType;
        }
    }

    public class VariableUsageInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int DeclarationLine { get; set; }
        public bool IsParameter { get; set; }
        public bool IsLocal { get; set; }
        public string ContainingMethod { get; set; }
        public int UsageCount => UsageLines.Count;
        public List<int> UsageLines { get; set; } = new();

        public VariableScope Scope { get; set; } = VariableScope.Local;
        public VariableLifetime Lifetime { get; set; } = VariableLifetime.Method;

        public VariableUsageInfo() { }

        public VariableUsageInfo(string name, string type, int declarationLine, bool isParameter, bool isLocal, string containingMethod)
        {
            Name = name;
            Type = type;
            DeclarationLine = declarationLine;
            IsParameter = isParameter;
            IsLocal = isLocal;
            ContainingMethod = containingMethod;
        }

        public void AddUsage(int lineNumber)
        {
            if (!UsageLines.Contains(lineNumber))
                UsageLines.Add(lineNumber);
        }
    }

    public class ElementDistance
    {
        public string SourceId { get; }
        public string TargetId { get; }
        public double Distance { get; }
        public double Similarity { get; }
        public int RelationshipType { get; }

        public ElementDistance(string sourceId, string targetId, double distance, double similarity, int relationshipType)
        {
            SourceId = sourceId;
            TargetId = targetId;
            Distance = distance;
            Similarity = similarity;
            RelationshipType = relationshipType;
        }
    }

    internal class LocationInfo
    {
        public SyntaxNode Node { get; }
        public string FilePath { get; }
        public int Line { get; }
        public int Column { get; }

        public LocationInfo(SyntaxNode node, string filePath = null)
        {
            Node = node;
            FilePath = filePath;
            var pos = node.GetLocation().GetLineSpan();
            Line = pos.StartLinePosition.Line + 1;
            Column = pos.StartLinePosition.Character + 1;
        }
    }
}
