using CodePathExplorer.Models;

namespace CodePathExplorer.Tests.Models;

public class CallChainNodeTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var node = new CallChainNode();

        node.MethodFullName.Should().Be("");
        node.ClassName.Should().Be("");
        node.MethodName.Should().Be("");
        node.LineNumber.Should().Be(0);
        node.IsBranchPoint.Should().BeFalse();
        node.BranchCondition.Should().BeNull();
        node.IsLoop.Should().BeFalse();
        node.LoopDescription.Should().BeNull();
    }

    [Fact]
    public void ToString_ReturnsMethodFullName()
    {
        var node = new CallChainNode { MethodFullName = "MyClass.MyMethod" };

        node.ToString().Should().Be("MyClass.MyMethod");
    }

    [Fact]
    public void Properties_SetAndGet_Correctly()
    {
        var node = new CallChainNode
        {
            MethodFullName = "TestClass.TestMethod",
            ClassName = "TestClass",
            MethodName = "TestMethod",
            LineNumber = 42,
            IsBranchPoint = true,
            BranchCondition = "x > 0",
            IsLoop = true,
            LoopDescription = "[循环回调用] TestClass.TestMethod"
        };

        node.MethodFullName.Should().Be("TestClass.TestMethod");
        node.ClassName.Should().Be("TestClass");
        node.MethodName.Should().Be("TestMethod");
        node.LineNumber.Should().Be(42);
        node.IsBranchPoint.Should().BeTrue();
        node.BranchCondition.Should().Be("x > 0");
        node.IsLoop.Should().BeTrue();
        node.LoopDescription.Should().Be("[循环回调用] TestClass.TestMethod");
    }
}

public class CallChainTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var chain = new CallChain();

        chain.ChainId.Should().Be(0);
        chain.EntryMethod.Should().Be("");
        chain.Nodes.Should().NotBeNull().And.BeEmpty();
        chain.Depth.Should().Be(0);
        chain.PathDescription.Should().Be("");
    }

    [Fact]
    public void Depth_WithNodes_ReturnsCorrectCount()
    {
        var chain = new CallChain();
        chain.Nodes.Add(new CallChainNode { MethodName = "A" });
        chain.Nodes.Add(new CallChainNode { MethodName = "B" });
        chain.Nodes.Add(new CallChainNode { MethodName = "C" });

        chain.Depth.Should().Be(3);
    }

    [Fact]
    public void PathDescription_WithNodes_ReturnsFormattedPath()
    {
        var chain = new CallChain();
        chain.Nodes.Add(new CallChainNode { MethodName = "A" });
        chain.Nodes.Add(new CallChainNode { MethodName = "B" });
        chain.Nodes.Add(new CallChainNode { MethodName = "C" });

        chain.PathDescription.Should().Be("A → B → C");
    }

    [Fact]
    public void PathDescription_WithBranchNode_IncludesCondition()
    {
        var chain = new CallChain();
        chain.Nodes.Add(new CallChainNode { MethodName = "A" });
        chain.Nodes.Add(new CallChainNode
        {
            MethodName = "B",
            IsBranchPoint = true,
            BranchCondition = "x > 0"
        });

        chain.PathDescription.Should().Be("A → B");
    }

    [Fact]
    public void PathDescription_WithLoopNode_IncludesLoopMarker()
    {
        var chain = new CallChain();
        chain.Nodes.Add(new CallChainNode { MethodName = "A" });
        chain.Nodes.Add(new CallChainNode
        {
            MethodName = "B",
            IsLoop = true,
            LoopDescription = "[循环回调用] A.B"
        });

        chain.PathDescription.Should().Be("A → B");
    }

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        var chain = new CallChain { ChainId = 5 };
        chain.Nodes.Add(new CallChainNode { MethodName = "Start" });
        chain.Nodes.Add(new CallChainNode { MethodName = "End" });

        chain.ToString().Should().Be("Chain 5: Start → End");
    }

    [Fact]
    public void PathDescription_EmptyNodes_ReturnsEmptyString()
    {
        var chain = new CallChain();

        chain.PathDescription.Should().Be("");
    }
}
