using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodePathExplorer.Explorers;
using CodePathExplorer.Models;
using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace CodePathExplorer.Tests.Explorers;

public class CallChainExplorerTests
{
    private const string SimpleClassCode = @"
class TestClass
{
    public void MethodA()
    {
        MethodB();
    }

    public void MethodB()
    {
        Console.WriteLine(""Hello"");
    }

    private void HelperMethod()
    {
        // private method
    }
}

class Program
{
    public static void Main() { }
}";

    private const string CodeWithBranch = @"
class TestClass
{
    public void Process(int x)
    {
        if (x > 0)
        {
            HandlePositive();
        }
        else
        {
            HandleNegative();
        }
    }

    public void HandlePositive() { }
    public void HandleNegative() { }
}";

    private const string CodeWithLoop = @"
class TestClass
{
    public void Start()
    {
        LoopMethod();
    }

    public void LoopMethod()
    {
        Start();
    }
}";

    private const string CodeWithSwitch = @"
class TestClass
{
    public void Process(string input)
    {
        switch (input)
        {
            case ""A"":
                HandleA();
                break;
            case ""B"":
                HandleB();
                break;
        }
    }

    public void HandleA() { }
    public void HandleB() { }
}";

    private const string CodeWithMultiplePublicMethods = @"
class TestClass
{
    public void Method1()
    {
        Method2();
    }

    public void Method2()
    {
        Method3();
    }

    public void Method3() { }

    internal void InternalMethod() { }

    private void PrivateMethod() { }
}";

    private const string CodeWithExternalCall = @"
class TestClass
{
    public void Start()
    {
        var result = ExternalMethod();
        Console.WriteLine(result);
    }

    public string ExternalMethod()
    {
        return ""test"";
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
    public void ExploreAll_WithSimpleClass_ReturnsChains()
    {
        var context = CreateContext(SimpleClassCode);
        var explorer = new CallChainExplorer(context);

        var chains = explorer.ExploreAll();

        chains.Should().NotBeNull();
        chains.Should().NotBeEmpty();
    }

    [Fact]
    public void ExploreAll_WithSimpleClass_ContainsPublicMethodsAsEntries()
    {
        var context = CreateContext(SimpleClassCode);
        var explorer = new CallChainExplorer(context);

        var chains = explorer.ExploreAll();

        var entryMethods = chains.Select(c => c.EntryMethod).Distinct().ToList();
        entryMethods.Should().Contain(m => m.Contains("MethodA"));
        entryMethods.Should().Contain(m => m.Contains("MethodB"));
    }

    [Fact]
    public void ExploreAll_PrivateMethod_NotIncludedAsEntry()
    {
        var context = CreateContext(SimpleClassCode);
        var explorer = new CallChainExplorer(context);

        var chains = explorer.ExploreAll();

        var entryMethods = chains.Select(c => c.EntryMethod).Distinct().ToList();
        entryMethods.Should().NotContain(m => m.Contains("HelperMethod"));
    }

    [Fact]
    public void ExploreFromMethod_WithValidMethod_ReturnsChain()
    {
        var context = CreateContext(SimpleClassCode);
        var explorer = new CallChainExplorer(context);

        var chains = explorer.ExploreFromMethod("TestClass.MethodA");

        chains.Should().NotBeEmpty();
        chains[0].EntryMethod.Should().Be("TestClass.MethodA");
    }

    [Fact]
    public void ExploreFromMethod_WithInvalidMethod_ReturnsEmpty()
    {
        var context = CreateContext(SimpleClassCode);
        var explorer = new CallChainExplorer(context);

        var chains = explorer.ExploreFromMethod("NonExistent.Method");

        chains.Should().BeEmpty();
    }

    [Fact]
    public void ExploreAll_WithBranch_GeneratesMultipleChains()
    {
        var context = CreateContext(CodeWithBranch);
        var explorer = new CallChainExplorer(context);

        var chains = explorer.ExploreAll();

        var processChains = chains.Where(c => c.EntryMethod.Contains("Process")).ToList();
        processChains.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void ExploreAll_WithBranch_ContainsBranchCondition()
    {
        var context = CreateContext(CodeWithBranch);
        var explorer = new CallChainExplorer(context);

        var chains = explorer.ExploreAll();

        var branchNodes = chains.SelectMany(c => c.Nodes).Where(n => n.IsBranchPoint).ToList();
        branchNodes.Should().NotBeEmpty();
    }

    [Fact]
    public void ExploreAll_WithLoop_DetectsCircularReference()
    {
        var context = CreateContext(CodeWithLoop);
        var explorer = new CallChainExplorer(context);

        var chains = explorer.ExploreAll();

        var loopNodes = chains.SelectMany(c => c.Nodes).Where(n => n.IsLoop).ToList();
        loopNodes.Should().NotBeEmpty();
    }

    [Fact]
    public void ExploreAll_WithSwitch_GeneratesMultipleChains()
    {
        var context = CreateContext(CodeWithSwitch);
        var explorer = new CallChainExplorer(context);

        var chains = explorer.ExploreAll();

        var processChains = chains.Where(c => c.EntryMethod.Contains("Process")).ToList();
        processChains.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void ExploreAll_WithMultiplePublicMethods_AllAreEntries()
    {
        var context = CreateContext(CodeWithMultiplePublicMethods);
        var explorer = new CallChainExplorer(context);

        var chains = explorer.ExploreAll();

        var entryMethods = chains.Select(c => c.EntryMethod).Distinct().ToList();
        entryMethods.Should().Contain(m => m.Contains("Method1"));
        entryMethods.Should().Contain(m => m.Contains("Method2"));
        entryMethods.Should().Contain(m => m.Contains("Method3"));
        entryMethods.Should().Contain(m => m.Contains("InternalMethod"));
        entryMethods.Should().NotContain(m => m.Contains("PrivateMethod"));
    }

    [Fact]
    public void ExploreAll_WithExternalCall_IncludesExternalNode()
    {
        var context = CreateContext(CodeWithExternalCall);
        var explorer = new CallChainExplorer(context);

        var chains = explorer.ExploreAll();

        var startChains = chains.Where(c => c.EntryMethod.Contains("Start")).ToList();
        startChains.Should().NotBeEmpty();
    }

    [Fact]
    public void ExploreAll_EmptyContext_ReturnsEmpty()
    {
        const string emptyCode = @"
class EmptyClass
{
    // No public methods
}";
        var context = CreateContext(emptyCode);
        var explorer = new CallChainExplorer(context);

        var chains = explorer.ExploreAll();

        chains.Should().BeEmpty();
    }

    [Fact]
    public void ExploreAll_ChainHasCorrectStructure()
    {
        var context = CreateContext(SimpleClassCode);
        var explorer = new CallChainExplorer(context);

        var chains = explorer.ExploreAll();

        foreach (var chain in chains)
        {
            chain.ChainId.Should().BeGreaterThan(0);
            chain.EntryMethod.Should().NotBeNullOrEmpty();
            chain.Nodes.Should().NotBeEmpty();
            chain.Depth.Should().Be(chain.Nodes.Count);
            chain.PathDescription.Should().NotBeNullOrEmpty();
        }
    }
}
