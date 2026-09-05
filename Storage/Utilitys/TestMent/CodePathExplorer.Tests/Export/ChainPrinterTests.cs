using CodePathExplorer.Export;
using CodePathExplorer.Models;

namespace CodePathExplorer.Tests.Export;

public class ChainPrinterTests
{
    [Fact]
    public void PrintReport_WithEmptyResult_ReturnsReport()
    {
        var result = new AnalysisResult
        {
            SourcePath = @"C:\Test",
            FileCount = 0,
            ClassCount = 0
        };
        var printer = new ChainPrinter();

        var report = printer.PrintReport(result);

        report.Should().NotBeNullOrEmpty();
        report.Should().Contain("CodePathExplorer 分析报告");
        report.Should().Contain(@"C:\Test");
    }

    [Fact]
    public void PrintReport_WithCallChains_IncludesChainInfo()
    {
        var result = new AnalysisResult
        {
            SourcePath = @"C:\Test",
            FileCount = 1,
            ClassCount = 1
        };
        var chain = new CallChain
        {
            ChainId = 1,
            EntryMethod = "TestClass.TestMethod"
        };
        chain.Nodes.Add(new CallChainNode
        {
            MethodFullName = "TestClass.TestMethod",
            ClassName = "TestClass",
            MethodName = "TestMethod"
        });
        result.CallChains.Add(chain);

        var printer = new ChainPrinter();
        var report = printer.PrintReport(result);

        report.Should().Contain("TestClass.TestMethod");
        report.Should().Contain("Chain 1");
    }

    [Fact]
    public void PrintReport_WithFieldFlows_IncludesFieldInfo()
    {
        var result = new AnalysisResult
        {
            SourcePath = @"C:\Test",
            FileCount = 1,
            ClassCount = 1
        };
        var fieldSet = new FieldFlowSet
        {
            FieldName = "_testField",
            FieldType = "int",
            ClassName = "TestClass"
        };
        var flow = new FieldFlow { FlowId = 1 };
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        fieldSet.Flows.Add(flow);
        result.FieldFlowSets.Add(fieldSet);

        var printer = new ChainPrinter();
        var report = printer.PrintReport(result);

        report.Should().Contain("_testField");
        report.Should().Contain("TestClass");
    }

    [Fact]
    public void PrintReport_WithSuggestions_IncludesSuggestionInfo()
    {
        var result = new AnalysisResult
        {
            SourcePath = @"C:\Test",
            FileCount = 1,
            ClassCount = 1
        };
        result.Suggestions.Add(new SimplificationSuggestion
        {
            Type = SuggestionType.RedundantField,
            Target = "_unusedField",
            ClassName = "TestClass",
            Description = "字段 '_unusedField' 未被任何方法使用",
            Detail = "类型: int, 类: TestClass"
        });

        var printer = new ChainPrinter();
        var report = printer.PrintReport(result);

        report.Should().Contain("RedundantField");
        report.Should().Contain("_unusedField");
        report.Should().Contain("未被任何方法使用");
    }

    [Fact]
    public void PrintReport_WithNoCallChains_ShowsEmptyMessage()
    {
        var result = new AnalysisResult();
        var printer = new ChainPrinter();

        var report = printer.PrintReport(result);

        report.Should().Contain("(无调用链)");
    }

    [Fact]
    public void PrintReport_WithNoFieldFlows_ShowsEmptyMessage()
    {
        var result = new AnalysisResult();
        var printer = new ChainPrinter();

        var report = printer.PrintReport(result);

        report.Should().Contain("(无字段)");
    }

    [Fact]
    public void PrintReport_WithNoSuggestions_ShowsEmptyMessage()
    {
        var result = new AnalysisResult();
        var printer = new ChainPrinter();

        var report = printer.PrintReport(result);

        report.Should().Contain("(无简化建议)");
    }

    [Fact]
    public void PrintReport_WithMultipleChains_GroupedByEntryMethod()
    {
        var result = new AnalysisResult();
        var chain1 = new CallChain { ChainId = 1, EntryMethod = "TestClass.MethodA" };
        chain1.Nodes.Add(new CallChainNode { MethodFullName = "TestClass.MethodA", MethodName = "MethodA" });
        result.CallChains.Add(chain1);

        var chain2 = new CallChain { ChainId = 2, EntryMethod = "TestClass.MethodA" };
        chain2.Nodes.Add(new CallChainNode { MethodFullName = "TestClass.MethodA", MethodName = "MethodA" });
        chain2.Nodes.Add(new CallChainNode { MethodFullName = "TestClass.MethodB", MethodName = "MethodB" });
        result.CallChains.Add(chain2);

        var printer = new ChainPrinter();
        var report = printer.PrintReport(result);

        report.Should().Contain("[入口] TestClass.MethodA");
        report.Should().Contain("Chain 1");
        report.Should().Contain("Chain 2");
    }
}
