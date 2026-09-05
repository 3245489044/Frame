using CodePathExplorer.Models;

namespace CodePathExplorer.Tests.Models;

public class FieldFlowNodeTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var node = new FieldFlowNode();

        node.OperationType.Should().Be(FieldOperationType.Declaration);
        node.MethodFullName.Should().Be("");
        node.ClassName.Should().Be("");
        node.MethodName.Should().Be("");
        node.LineNumber.Should().Be(0);
        node.PassedToMethod.Should().BeNull();
    }

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        var node = new FieldFlowNode
        {
            OperationType = FieldOperationType.Read,
            MethodFullName = "MyClass.MyMethod"
        };

        node.ToString().Should().Be("[Read] MyClass.MyMethod");
    }

    [Fact]
    public void Properties_SetAndGet_Correctly()
    {
        var node = new FieldFlowNode
        {
            OperationType = FieldOperationType.Write,
            MethodFullName = "TestClass.TestMethod",
            ClassName = "TestClass",
            MethodName = "TestMethod",
            LineNumber = 10,
            PassedToMethod = "AnotherMethod"
        };

        node.OperationType.Should().Be(FieldOperationType.Write);
        node.MethodFullName.Should().Be("TestClass.TestMethod");
        node.ClassName.Should().Be("TestClass");
        node.MethodName.Should().Be("TestMethod");
        node.LineNumber.Should().Be(10);
        node.PassedToMethod.Should().Be("AnotherMethod");
    }
}

public class FieldFlowTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var flow = new FieldFlow();

        flow.FlowId.Should().Be(0);
        flow.FieldName.Should().Be("");
        flow.FieldType.Should().Be("");
        flow.ClassName.Should().Be("");
        flow.Nodes.Should().NotBeNull().And.BeEmpty();
        flow.FlowDescription.Should().Be("");
    }

    [Fact]
    public void FlowDescription_WithNodes_ReturnsFormattedPath()
    {
        var flow = new FieldFlow();
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Read,
            MethodName = "Getter"
        });

        flow.FlowDescription.Should().Be("[Declaration] (字段声明) → [Read] Getter");
    }

    [Fact]
    public void FlowDescription_WithPassAsArgument_IncludesTarget()
    {
        var flow = new FieldFlow();
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.PassAsArgument,
            MethodName = "Caller",
            PassedToMethod = "TargetMethod"
        });

        flow.FlowDescription.Should().Be("[Declaration] (字段声明) → [PassAsArgument] Caller");
    }

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        var flow = new FieldFlow { FlowId = 3 };
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });

        flow.ToString().Should().Be("Flow 3: [Declaration] (字段声明)");
    }
}

public class FieldFlowSetTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var set = new FieldFlowSet();

        set.FieldName.Should().Be("");
        set.FieldType.Should().Be("");
        set.ClassName.Should().Be("");
        set.Flows.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void IsUnused_WithNoFlows_ReturnsTrue()
    {
        var set = new FieldFlowSet();

        set.IsUnused.Should().BeTrue();
    }

    [Fact]
    public void IsUnused_WithOnlyDeclarationFlows_ReturnsTrue()
    {
        var set = new FieldFlowSet();
        var flow = new FieldFlow();
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        set.Flows.Add(flow);

        set.IsUnused.Should().BeTrue();
    }

    [Fact]
    public void IsUnused_WithReadOperation_ReturnsFalse()
    {
        var set = new FieldFlowSet();
        var flow = new FieldFlow();
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Read,
            MethodName = "Getter"
        });
        set.Flows.Add(flow);

        set.IsUnused.Should().BeFalse();
    }

    [Fact]
    public void IsUnused_WithWriteOperation_ReturnsFalse()
    {
        var set = new FieldFlowSet();
        var flow = new FieldFlow();
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Write,
            MethodName = "Setter"
        });
        set.Flows.Add(flow);

        set.IsUnused.Should().BeFalse();
    }

    [Fact]
    public void IsSingleMethodUse_WithSingleMethod_ReturnsTrue()
    {
        var set = new FieldFlowSet();
        var flow1 = new FieldFlow();
        flow1.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        flow1.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Read,
            MethodName = "MethodA"
        });
        set.Flows.Add(flow1);

        var flow2 = new FieldFlow();
        flow2.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        flow2.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Write,
            MethodName = "MethodA"
        });
        set.Flows.Add(flow2);

        set.IsSingleMethodUse.Should().BeTrue();
    }

    [Fact]
    public void IsSingleMethodUse_WithMultipleMethods_ReturnsFalse()
    {
        var set = new FieldFlowSet();
        var flow1 = new FieldFlow();
        flow1.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        flow1.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Read,
            MethodName = "MethodA"
        });
        set.Flows.Add(flow1);

        var flow2 = new FieldFlow();
        flow2.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        flow2.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Write,
            MethodName = "MethodB"
        });
        set.Flows.Add(flow2);

        set.IsSingleMethodUse.Should().BeFalse();
    }

    [Fact]
    public void IsSingleMethodUse_WithNoNonDeclarationNodes_ReturnsTrue()
    {
        var set = new FieldFlowSet();
        var flow = new FieldFlow();
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        set.Flows.Add(flow);

        set.IsSingleMethodUse.Should().BeTrue();
    }

    [Fact]
    public void InvalidateCache_ResetsCachedValues()
    {
        var set = new FieldFlowSet();
        var flow = new FieldFlow();
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        set.Flows.Add(flow);

        var unused1 = set.IsUnused;
        unused1.Should().BeTrue();

        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Read,
            MethodName = "MethodA"
        });

        set.IsUnused.Should().BeTrue();

        set.InvalidateCache();

        set.IsUnused.Should().BeFalse();
    }

    [Fact]
    public void InvalidateCache_ResetsSingleMethodUseCache()
    {
        var set = new FieldFlowSet();
        var flow = new FieldFlow();
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Read,
            MethodName = "MethodA"
        });
        set.Flows.Add(flow);

        var singleUse1 = set.IsSingleMethodUse;
        singleUse1.Should().BeTrue();

        var flow2 = new FieldFlow();
        flow2.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        flow2.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Write,
            MethodName = "MethodB"
        });
        set.Flows.Add(flow2);

        set.IsSingleMethodUse.Should().BeTrue();

        set.InvalidateCache();

        set.IsSingleMethodUse.Should().BeFalse();
    }
}

public class FieldOperationTypeTests
{
    [Fact]
    public void FieldOperationType_Values_AreDefined()
    {
        Enum.GetValues<FieldOperationType>().Should().HaveCount(4);
        Enum.GetName(FieldOperationType.Declaration).Should().Be("Declaration");
        Enum.GetName(FieldOperationType.Read).Should().Be("Read");
        Enum.GetName(FieldOperationType.Write).Should().Be("Write");
        Enum.GetName(FieldOperationType.PassAsArgument).Should().Be("PassAsArgument");
    }
}
