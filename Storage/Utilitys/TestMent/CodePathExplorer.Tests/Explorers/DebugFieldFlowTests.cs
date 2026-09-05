using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodePathExplorer.Models;
using CodePathExplorer.Explorers;
using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace CodePathExplorer.Tests.Explorers;

public class DebugFieldFlowTests
{
    private const string CodeWithLocalVariableShadowing = @"
class TestClass
{
    private int _value;

    public void Test()
    {
        int _value = 10;
        Console.WriteLine(_value);
    }

    public void UseField()
    {
        _value = 42;
    }
}";

    private const string CodeWithThisPrefix = @"
class TestClass
{
    private int _value;

    public void Test()
    {
        this._value = 10;
    }
}";

    private AnalysisContext CreateContext(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var compilation = CSharpCompilation.Create("Test")
            .AddSyntaxTrees(tree)
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        return new AnalysisContext
        {
            FilePath = "test.cs",
            SourceCode = code,
            SyntaxTree = tree,
            Root = root,
            SemanticModel = compilation.GetSemanticModel(tree)
        };
    }

    [Fact]
    public void Debug_LocalVariableShadowing_ExploreAll()
    {
        var context = CreateContext(CodeWithLocalVariableShadowing);
        var explorer = new FieldFlowExplorer(context);
        var result = explorer.ExploreAll();

        System.Console.WriteLine($"Total flow sets: {result.Count}");
        foreach (var flowSet in result)
        {
            System.Console.WriteLine($"  Field: {flowSet.FieldName}, Class: {flowSet.ClassName}");
            System.Console.WriteLine($"  Flows count: {flowSet.Flows.Count}");
            foreach (var flow in flowSet.Flows)
            {
                System.Console.WriteLine($"    FlowId: {flow.FlowId}");
                foreach (var node in flow.Nodes)
                {
                    System.Console.WriteLine($"      Node: Op={node.OperationType}, Method={node.MethodName}, FullName={node.MethodFullName}");
                }
            }
        }

        var valueFlow = result.FirstOrDefault(f => f.FieldName == "_value");
        valueFlow.Should().NotBeNull();

        var useFieldNodes = valueFlow!.Flows
            .SelectMany(f => f.Nodes)
            .Where(n => n.MethodName == "UseField")
            .ToList();
        System.Console.WriteLine($"\nUseField nodes count: {useFieldNodes.Count}");
        useFieldNodes.Should().NotBeEmpty();
    }

    [Fact]
    public void Debug_ThisPrefix_ExploreAll()
    {
        var context = CreateContext(CodeWithThisPrefix);
        var explorer = new FieldFlowExplorer(context);
        var result = explorer.ExploreAll();

        System.Console.WriteLine($"Total flow sets: {result.Count}");
        foreach (var flowSet in result)
        {
            System.Console.WriteLine($"  Field: {flowSet.FieldName}, Class: {flowSet.ClassName}");
            System.Console.WriteLine($"  Flows count: {flowSet.Flows.Count}");
            foreach (var flow in flowSet.Flows)
            {
                System.Console.WriteLine($"    FlowId: {flow.FlowId}");
                foreach (var node in flow.Nodes)
                {
                    System.Console.WriteLine($"      Node: Op={node.OperationType}, Method={node.MethodName}, FullName={node.MethodFullName}");
                }
            }
        }

        var valueFlow = result.FirstOrDefault(f => f.FieldName == "_value");
        valueFlow.Should().NotBeNull();

        var writeNodes = valueFlow!.Flows
            .SelectMany(f => f.Nodes)
            .Where(n => n.OperationType == FieldOperationType.Write)
            .ToList();
        System.Console.WriteLine($"\nWrite nodes count: {writeNodes.Count}");
        writeNodes.Should().NotBeEmpty();
    }
}
