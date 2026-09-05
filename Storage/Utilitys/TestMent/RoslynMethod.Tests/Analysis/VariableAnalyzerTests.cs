using RoslynSpace;

namespace RoslynMethod.Tests.Analysis;

public class VariableAnalyzerTests
{
    private const string TestCode = @"
using System;
namespace TestNamespace
{
    public class TestClass
    {
        public void MethodWithVariables()
        {
            int x = 10;
            string name = ""test"";
            var result = x + 5;
            Console.WriteLine(result);
        }

        public void MethodWithParams(int a, string b)
        {
            Console.WriteLine(a + b);
        }

        public int AutoProp { get; set; }
    }
}";

    [Fact]
    public void AnalyzeVariables_WithValidCode_ReturnsVariableMap()
    {
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(TestCode).GetRoot();

        var variableMap = VariableAnalyzer.AnalyzeVariables(root);

        variableMap.Should().NotBeEmpty();
    }

    [Fact]
    public void AnalyzeVariables_ContainsMethodVariables()
    {
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(TestCode).GetRoot();

        var variableMap = VariableAnalyzer.AnalyzeVariables(root);

        variableMap.Keys.Should().Contain(k => k.Contains("MethodWithVariables"));
    }

    [Fact]
    public void AnalyzeVariables_ContainsParameterVariables()
    {
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(TestCode).GetRoot();

        var variableMap = VariableAnalyzer.AnalyzeVariables(root);

        variableMap.Keys.Should().Contain(k => k.Contains("MethodWithParams"));
    }

    [Fact]
    public void AnalyzeVariables_WithEmptyCode_ReturnsEmptyMap()
    {
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("").GetRoot();

        var variableMap = VariableAnalyzer.AnalyzeVariables(root);

        variableMap.Should().BeEmpty();
    }

    [Fact]
    public void UpdateElementVariables_WithMatchingData_UpdatesElements()
    {
        var elements = SyntaxElementExtractor.Extract(TestCode);
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(TestCode).GetRoot();
        var variableMap = VariableAnalyzer.AnalyzeVariables(root);

        VariableAnalyzer.UpdateElementVariables(elements, variableMap);

        var methodElement = elements.FirstOrDefault(e => e.Name == "MethodWithVariables");
        if (methodElement != null)
        {
            methodElement.Variables.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void CalculateVariableMetrics_WithElements_CalculatesMetrics()
    {
        var elements = SyntaxElementExtractor.Extract(TestCode);
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(TestCode).GetRoot();
        var variableMap = VariableAnalyzer.AnalyzeVariables(root);
        VariableAnalyzer.UpdateElementVariables(elements, variableMap);

        VariableAnalyzer.CalculateVariableMetrics(elements);

        var methodElement = elements.FirstOrDefault(e => e.Name == "MethodWithVariables");
        if (methodElement != null)
        {
            methodElement.VariableReuseRatio.Should().BeGreaterOrEqualTo(0);
        }
    }

    [Fact]
    public void ReferenceAnalyzer_Analyze_WithElements_UpdatesReferences()
    {
        var elements = SyntaxElementExtractor.Extract(TestCode);
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(TestCode).GetRoot();

        ReferenceAnalyzer.Analyze(elements, root);

        elements.Should().AllSatisfy(e => e.ReferencedBy.Should().NotBeNull());
    }
}
