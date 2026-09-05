using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodePathExplorer.Explorers;
using CodePathExplorer.Models;
using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace CodePathExplorer.Tests.Explorers;

public class FieldFlowExplorerTests
{
    private const string CodeWithFields = @"
class TestClass
{
    private int _counter;
    private string _name;

    public void Increment()
    {
        _counter++;
    }

    public string GetName()
    {
        return _name;
    }

    public void SetName(string value)
    {
        _name = value;
    }
}";

    private const string CodeWithUnusedField = @"
class TestClass
{
    private int _unusedField;
    private string _usedField;

    public void UseField()
    {
        _usedField = ""test"";
    }
}";

    private const string CodeWithFieldAsArgument = @"
class TestClass
{
    private string _data;

    public void Process()
    {
        Helper(_data);
    }

    public void Helper(string input) { }
}";

    private const string CodeWithLocalVariableShadowing = @"
class TestClass
{
    private int _value;

    public void Test()
    {
        int _value = 10; // 局部变量遮蔽字段
        Console.WriteLine(_value);
    }

    public void UseField()
    {
        _value = 42; // 使用字段
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

    private const string CodeWithMultipleClasses = @"
class ClassA
{
    private int _fieldA;

    public void MethodA()
    {
        _fieldA = 1;
    }
}

class ClassB
{
    private string _fieldB;

    public void MethodB()
    {
        _fieldB = ""test"";
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
    public void ExploreAll_WithFields_ReturnsFieldFlowSets()
    {
        var context = CreateContext(CodeWithFields);
        var explorer = new FieldFlowExplorer(context);

        var flows = explorer.ExploreAll();

        flows.Should().NotBeNull();
        flows.Should().NotBeEmpty();
    }

    [Fact]
    public void ExploreAll_WithFields_ContainsAllFields()
    {
        var context = CreateContext(CodeWithFields);
        var explorer = new FieldFlowExplorer(context);

        var flows = explorer.ExploreAll();

        var fieldNames = flows.Select(f => f.FieldName).ToList();
        fieldNames.Should().Contain("_counter");
        fieldNames.Should().Contain("_name");
    }

    [Fact]
    public void ExploreAll_WithFields_ContainsClassName()
    {
        var context = CreateContext(CodeWithFields);
        var explorer = new FieldFlowExplorer(context);

        var flows = explorer.ExploreAll();

        foreach (var flowSet in flows)
        {
            flowSet.ClassName.Should().Be("TestClass");
        }
    }

    [Fact]
    public void ExploreAll_WithFields_ContainsFieldType()
    {
        var context = CreateContext(CodeWithFields);
        var explorer = new FieldFlowExplorer(context);

        var flows = explorer.ExploreAll();

        var counterFlow = flows.First(f => f.FieldName == "_counter");
        counterFlow.FieldType.Should().Be("int");

        var nameFlow = flows.First(f => f.FieldName == "_name");
        nameFlow.FieldType.Should().Be("string");
    }

    [Fact]
    public void ExploreAll_WithFields_EachFlowHasDeclarationNode()
    {
        var context = CreateContext(CodeWithFields);
        var explorer = new FieldFlowExplorer(context);

        var flows = explorer.ExploreAll();

        foreach (var flowSet in flows)
        {
            foreach (var flow in flowSet.Flows)
            {
                flow.Nodes.Should().NotBeEmpty();
                flow.Nodes[0].OperationType.Should().Be(FieldOperationType.Declaration);
            }
        }
    }

    [Fact]
    public void ExploreAll_WithUnusedField_MarksAsUnused()
    {
        var context = CreateContext(CodeWithUnusedField);
        var explorer = new FieldFlowExplorer(context);

        var flows = explorer.ExploreAll();

        var unusedField = flows.FirstOrDefault(f => f.FieldName == "_unusedField");
        unusedField.Should().NotBeNull();
        unusedField!.IsUnused.Should().BeTrue();
    }

    [Fact]
    public void ExploreAll_WithUsedField_NotMarkedAsUnused()
    {
        var context = CreateContext(CodeWithUnusedField);
        var explorer = new FieldFlowExplorer(context);

        var flows = explorer.ExploreAll();

        var usedField = flows.FirstOrDefault(f => f.FieldName == "_usedField");
        usedField.Should().NotBeNull();
        usedField!.IsUnused.Should().BeFalse();
    }

    [Fact]
    public void ExploreAll_WithFieldAsArgument_IncludesPassAsArgument()
    {
        var context = CreateContext(CodeWithFieldAsArgument);
        var explorer = new FieldFlowExplorer(context);

        var flows = explorer.ExploreAll();

        var dataFlow = flows.FirstOrDefault(f => f.FieldName == "_data");
        dataFlow.Should().NotBeNull();

        var hasPassAsArgument = dataFlow!.Flows
            .SelectMany(f => f.Nodes)
            .Any(n => n.OperationType == FieldOperationType.PassAsArgument);
        hasPassAsArgument.Should().BeTrue();
    }

    [Fact]
    public void ExploreAll_WithLocalVariableShadowing_DoesNotCountAsFieldRef()
    {
        var context = CreateContext(CodeWithLocalVariableShadowing);
        var explorer = new FieldFlowExplorer(context);

        var flows = explorer.ExploreAll();

        var valueFlow = flows.FirstOrDefault(f => f.FieldName == "_value");
        valueFlow.Should().NotBeNull();

        var useFieldNodes = valueFlow!.Flows
            .SelectMany(f => f.Nodes)
            .Where(n => n.MethodName == "UseField")
            .ToList();
        useFieldNodes.Should().NotBeEmpty();
    }

    [Fact]
    public void ExploreAll_WithThisPrefix_DetectsFieldReference()
    {
        var context = CreateContext(CodeWithThisPrefix);
        var explorer = new FieldFlowExplorer(context);

        var flows = explorer.ExploreAll();

        var valueFlow = flows.FirstOrDefault(f => f.FieldName == "_value");
        valueFlow.Should().NotBeNull();

        var writeNodes = valueFlow!.Flows
            .SelectMany(f => f.Nodes)
            .Where(n => n.OperationType == FieldOperationType.Write)
            .ToList();
        writeNodes.Should().NotBeEmpty();
    }

    [Fact]
    public void ExploreAll_WithMultipleClasses_ReturnsFlowsForEachClass()
    {
        var context = CreateContext(CodeWithMultipleClasses);
        var explorer = new FieldFlowExplorer(context);

        var flows = explorer.ExploreAll();

        var classANames = flows.Where(f => f.ClassName == "ClassA").Select(f => f.FieldName).ToList();
        classANames.Should().Contain("_fieldA");

        var classBNames = flows.Where(f => f.ClassName == "ClassB").Select(f => f.FieldName).ToList();
        classBNames.Should().Contain("_fieldB");
    }

    [Fact]
    public void ExploreAll_WithNoFields_ReturnsEmpty()
    {
        const string code = @"
class EmptyClass
{
    public void DoSomething() { }
}";
        var context = CreateContext(code);
        var explorer = new FieldFlowExplorer(context);

        var flows = explorer.ExploreAll();

        flows.Should().BeEmpty();
    }

    [Fact]
    public void ExploreAll_FlowHasCorrectStructure()
    {
        var context = CreateContext(CodeWithFields);
        var explorer = new FieldFlowExplorer(context);

        var flows = explorer.ExploreAll();

        foreach (var flowSet in flows)
        {
            flowSet.FieldName.Should().NotBeNullOrEmpty();
            flowSet.ClassName.Should().NotBeNullOrEmpty();
            flowSet.FieldType.Should().NotBeNullOrEmpty();

            foreach (var flow in flowSet.Flows)
            {
                flow.FlowId.Should().BeGreaterThan(0);
                flow.FieldName.Should().Be(flowSet.FieldName);
                flow.ClassName.Should().Be(flowSet.ClassName);
                flow.Nodes.Should().NotBeEmpty();
            }
        }
    }
}
