using CodePathExplorer.Export;
using CodePathExplorer.Models;

namespace CodePathExplorer.Tests.Export;

public class MarkdownExporterTests
{
    [Fact]
    public void ExportToMarkdown_WithEmptyResult_ReturnsMarkdown()
    {
        var result = new AnalysisResult
        {
            SourcePath = @"C:\Test",
            FileCount = 0,
            ClassCount = 0
        };
        var exporter = new MarkdownExporter();

        var markdown = exporter.ExportToMarkdown(result);

        markdown.Should().NotBeNullOrEmpty();
        markdown.Should().Contain("# CodePathExplorer 分析报告");
        markdown.Should().Contain(@"C:\Test");
    }

    [Fact]
    public void ExportToMarkdown_WithCallChains_IncludesChainTable()
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

        var exporter = new MarkdownExporter();
        var markdown = exporter.ExportToMarkdown(result);

        markdown.Should().Contain("## 函数调用链");
        markdown.Should().Contain("TestClass.TestMethod");
        markdown.Should().Contain("| Chain ID |");
    }

    [Fact]
    public void ExportToMarkdown_WithFieldFlows_IncludesFieldSection()
    {
        var result = new AnalysisResult();
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

        var exporter = new MarkdownExporter();
        var markdown = exporter.ExportToMarkdown(result);

        markdown.Should().Contain("## 字段数据流");
        markdown.Should().Contain("_testField");
        markdown.Should().Contain("| Flow ID |");
    }

    [Fact]
    public void ExportToMarkdown_WithSuggestions_IncludesSuggestionSection()
    {
        var result = new AnalysisResult();
        result.Suggestions.Add(new SimplificationSuggestion
        {
            Type = SuggestionType.RedundantField,
            Target = "_unusedField",
            ClassName = "TestClass",
            Description = "字段 '_unusedField' 未被任何方法使用",
            Detail = "类型: int, 类: TestClass"
        });

        var exporter = new MarkdownExporter();
        var markdown = exporter.ExportToMarkdown(result);

        markdown.Should().Contain("## 简化建议");
        markdown.Should().Contain("_unusedField");
        markdown.Should().Contain("未被任何方法使用");
    }

    [Fact]
    public void ExportToMarkdown_WithNoCallChains_ShowsEmptyMessage()
    {
        var result = new AnalysisResult();
        var exporter = new MarkdownExporter();

        var markdown = exporter.ExportToMarkdown(result);

        markdown.Should().Contain("*(无调用链)*");
    }

    [Fact]
    public void ExportToMarkdown_WithNoFieldFlows_ShowsEmptyMessage()
    {
        var result = new AnalysisResult();
        var exporter = new MarkdownExporter();

        var markdown = exporter.ExportToMarkdown(result);

        markdown.Should().Contain("*(无字段)*");
    }

    [Fact]
    public void ExportToMarkdown_WithNoSuggestions_ShowsEmptyMessage()
    {
        var result = new AnalysisResult();
        var exporter = new MarkdownExporter();

        var markdown = exporter.ExportToMarkdown(result);

        markdown.Should().Contain("*(无简化建议)*");
    }

    [Fact]
    public void ExportToMarkdown_WithMultipleChains_GroupedByEntryMethod()
    {
        var result = new AnalysisResult();
        var chain1 = new CallChain { ChainId = 1, EntryMethod = "TestClass.MethodA" };
        chain1.Nodes.Add(new CallChainNode { MethodFullName = "TestClass.MethodA", MethodName = "MethodA" });
        result.CallChains.Add(chain1);

        var chain2 = new CallChain { ChainId = 2, EntryMethod = "TestClass.MethodA" };
        chain2.Nodes.Add(new CallChainNode { MethodFullName = "TestClass.MethodA", MethodName = "MethodA" });
        chain2.Nodes.Add(new CallChainNode { MethodFullName = "TestClass.MethodB", MethodName = "MethodB" });
        result.CallChains.Add(chain2);

        var exporter = new MarkdownExporter();
        var markdown = exporter.ExportToMarkdown(result);

        markdown.Should().Contain("### 入口: TestClass.MethodA");
        markdown.Should().Contain("| 1 | MethodA |");
        markdown.Should().Contain("| 2 | MethodA → MethodB |");
    }

    [Fact]
    public void ExportToMarkdown_WithUnusedField_IncludesStatus()
    {
        var result = new AnalysisResult();
        var fieldSet = new FieldFlowSet
        {
            FieldName = "_unusedField",
            FieldType = "int",
            ClassName = "TestClass"
        };
        var flow = new FieldFlow();
        flow.Nodes.Add(new FieldFlowNode
        {
            OperationType = FieldOperationType.Declaration,
            MethodName = "(字段声明)"
        });
        fieldSet.Flows.Add(flow);
        result.FieldFlowSets.Add(fieldSet);

        var exporter = new MarkdownExporter();
        var markdown = exporter.ExportToMarkdown(result);

        markdown.Should().Contain("⚠");
    }

    [Fact]
    public void ExportToMarkdown_IncludesSummaryStats()
    {
        var result = new AnalysisResult
        {
            SourcePath = @"C:\Project",
            FileCount = 10,
            ClassCount = 5
        };
        result.CallChains.Add(new CallChain { ChainId = 1, EntryMethod = "Test.Method" });
        result.FieldFlowSets.Add(new FieldFlowSet { FieldName = "f1" });

        var exporter = new MarkdownExporter();
        var markdown = exporter.ExportToMarkdown(result);

        markdown.Should().Contain("10");
        markdown.Should().Contain("5");
        markdown.Should().Contain("1");
        markdown.Should().Contain("1");
    }
}
