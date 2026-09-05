using CodePathExplorer.Export;
using CodePathExplorer.Models;

namespace CodePathExplorer.Tests.Export;

public class FormatHelperTests
{
    [Fact]
    public void FormatChainNode_WithNormalNode_ReturnsMethodName()
    {
        var node = new CallChainNode { MethodName = "TestMethod" };

        var result = FormatHelper.FormatChainNode(node);

        result.Should().Be("TestMethod");
    }

    [Fact]
    public void FormatChainNode_WithLoopNode_IncludesLoopPrefix()
    {
        var node = new CallChainNode
        {
            MethodName = "TestMethod",
            IsLoop = true
        };

        var result = FormatHelper.FormatChainNode(node);

        result.Should().Be("[循环] TestMethod");
    }

    [Fact]
    public void FormatChainNode_WithBranchNode_IncludesCondition()
    {
        var node = new CallChainNode
        {
            MethodName = "TestMethod",
            IsBranchPoint = true,
            BranchCondition = "x > 0"
        };

        var result = FormatHelper.FormatChainNode(node);

        result.Should().Be("TestMethod (x > 0)");
    }

    [Fact]
    public void FormatChainPath_WithMultipleNodes_ReturnsArrowSeparated()
    {
        var chain = new CallChain();
        chain.Nodes.Add(new CallChainNode { MethodName = "A" });
        chain.Nodes.Add(new CallChainNode { MethodName = "B" });
        chain.Nodes.Add(new CallChainNode { MethodName = "C" });

        var result = FormatHelper.FormatChainPath(chain);

        result.Should().Be("A → B → C");
    }

    [Fact]
    public void FormatChainPath_WithEmptyChain_ReturnsEmpty()
    {
        var chain = new CallChain();

        var result = FormatHelper.FormatChainPath(chain);

        result.Should().Be("");
    }

    [Fact]
    public void GetOperationSymbol_ForDeclaration_ReturnsCorrectSymbol()
    {
        FormatHelper.GetOperationSymbol(FieldOperationType.Declaration).Should().Be("声明");
    }

    [Fact]
    public void GetOperationSymbol_ForRead_ReturnsCorrectSymbol()
    {
        FormatHelper.GetOperationSymbol(FieldOperationType.Read).Should().Be("读");
    }

    [Fact]
    public void GetOperationSymbol_ForWrite_ReturnsCorrectSymbol()
    {
        FormatHelper.GetOperationSymbol(FieldOperationType.Write).Should().Be("写");
    }

    [Fact]
    public void GetOperationSymbol_ForPassAsArgument_ReturnsCorrectSymbol()
    {
        FormatHelper.GetOperationSymbol(FieldOperationType.PassAsArgument).Should().Be("传参");
    }

    [Fact]
    public void FormatFieldFlowNode_WithNormalNode_ReturnsFormattedString()
    {
        var node = new FieldFlowNode
        {
            OperationType = FieldOperationType.Read,
            MethodName = "Getter"
        };

        var result = FormatHelper.FormatFieldFlowNode(node);

        result.Should().Be("[读] Getter");
    }

    [Fact]
    public void FormatFieldFlowNode_WithPassAsArgument_IncludesTargetMethod()
    {
        var node = new FieldFlowNode
        {
            OperationType = FieldOperationType.PassAsArgument,
            MethodName = "Caller",
            PassedToMethod = "TargetMethod"
        };

        var result = FormatHelper.FormatFieldFlowNode(node);

        result.Should().Be("[传参] Caller → TargetMethod");
    }

    [Fact]
    public void FormatFieldFlowPath_WithMultipleNodes_ReturnsArrowSeparated()
    {
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

        var result = FormatHelper.FormatFieldFlowPath(flow);

        result.Should().Be("[声明] (字段声明) → [写] Setter");
    }

    [Fact]
    public void GetFieldSetStatus_WithUnusedField_ReturnsWarning()
    {
        var fieldSet = new FieldFlowSet();
        var flow = new FieldFlow();
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        fieldSet.Flows.Add(flow);

        var result = FormatHelper.GetFieldSetStatus(fieldSet);

        result.Should().Be(" ⚠ 冗余");
    }

    [Fact]
    public void GetFieldSetStatus_WithSingleMethodUse_ReturnsLightning()
    {
        var fieldSet = new FieldFlowSet();
        var flow = new FieldFlow();
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Read,
            MethodName = "SingleUser"
        });
        fieldSet.Flows.Add(flow);

        var result = FormatHelper.GetFieldSetStatus(fieldSet);

        result.Should().Be(" ⚡ 可内化");
    }

    [Fact]
    public void GetFieldSetStatus_WithNormalField_ReturnsEmpty()
    {
        var fieldSet = new FieldFlowSet();
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
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Write,
            MethodName = "MethodB"
        });
        fieldSet.Flows.Add(flow);

        var result = FormatHelper.GetFieldSetStatus(fieldSet);

        result.Should().Be("");
    }

    [Fact]
    public void GetSuggestionIcon_ForRedundantField_ReturnsWarning()
    {
        FormatHelper.GetSuggestionIcon(SuggestionType.RedundantField).Should().Be("⚠");
    }

    [Fact]
    public void GetSuggestionIcon_ForInternalizableField_ReturnsLightning()
    {
        FormatHelper.GetSuggestionIcon(SuggestionType.InternalizableField).Should().Be("⚡");
    }

    [Fact]
    public void GetSuggestionIcon_ForRedundantMethod_ReturnsWarning()
    {
        FormatHelper.GetSuggestionIcon(SuggestionType.RedundantMethod).Should().Be("⚠");
    }

    [Fact]
    public void GetSuggestionIcon_ForSimplifiable_ReturnsBulb()
    {
        FormatHelper.GetSuggestionIcon(SuggestionType.Simplifiable).Should().Be("💡");
    }

    [Fact]
    public void GetSuggestionMarkdownIcon_ForRedundantField_ReturnsWarning()
    {
        FormatHelper.GetSuggestionMarkdownIcon(SuggestionType.RedundantField).Should().Be("⚠️");
    }

    [Fact]
    public void GetSuggestionMarkdownIcon_ForInternalizableField_ReturnsLightning()
    {
        FormatHelper.GetSuggestionMarkdownIcon(SuggestionType.InternalizableField).Should().Be("⚡");
    }

    [Fact]
    public void GetSuggestionMarkdownIcon_ForRedundantMethod_ReturnsWarning()
    {
        FormatHelper.GetSuggestionMarkdownIcon(SuggestionType.RedundantMethod).Should().Be("⚠️");
    }

    [Fact]
    public void GetSuggestionMarkdownIcon_ForSimplifiable_ReturnsBulb()
    {
        FormatHelper.GetSuggestionMarkdownIcon(SuggestionType.Simplifiable).Should().Be("💡");
    }
}
