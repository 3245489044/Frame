using CodePathExplorer.Explorers;
using CodePathExplorer.Models;

namespace CodePathExplorer.Tests.Explorers;

public class SimplificationAnalyzerTests
{
    [Fact]
    public void Analyze_WithRedundantField_ReturnsRedundantFieldSuggestion()
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

        var analyzer = new SimplificationAnalyzer();
        var suggestions = analyzer.Analyze(result);

        suggestions.Should().Contain(s =>
            s.Type == SuggestionType.RedundantField &&
            s.Target == "_unusedField" &&
            s.ClassName == "TestClass");
    }

    [Fact]
    public void Analyze_WithInternalizableField_ReturnsInternalizableSuggestion()
    {
        var result = new AnalysisResult();
        var fieldSet = new FieldFlowSet
        {
            FieldName = "_localField",
            FieldType = "string",
            ClassName = "TestClass"
        };
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
        result.FieldFlowSets.Add(fieldSet);

        var analyzer = new SimplificationAnalyzer();
        var suggestions = analyzer.Analyze(result);

        suggestions.Should().Contain(s =>
            s.Type == SuggestionType.InternalizableField &&
            s.Target == "_localField" &&
            s.ClassName == "TestClass");
    }

    [Fact]
    public void Analyze_WithRedundantMethod_ReturnsRedundantMethodSuggestion()
    {
        var result = new AnalysisResult();
        var chain = new CallChain
        {
            ChainId = 1,
            EntryMethod = "TestClass.OrphanMethod"
        };
        chain.Nodes.Add(new CallChainNode
        {
            MethodFullName = "TestClass.OrphanMethod",
            ClassName = "TestClass",
            MethodName = "OrphanMethod"
        });
        result.CallChains.Add(chain);

        var analyzer = new SimplificationAnalyzer();
        var suggestions = analyzer.Analyze(result);

        suggestions.Should().Contain(s =>
            s.Type == SuggestionType.RedundantMethod &&
            s.Target.Contains("OrphanMethod"));
    }

    [Fact]
    public void Analyze_WithMainMethod_DoesNotMarkAsRedundant()
    {
        var result = new AnalysisResult();
        var chain = new CallChain
        {
            ChainId = 1,
            EntryMethod = "Program.Main"
        };
        chain.Nodes.Add(new CallChainNode
        {
            MethodFullName = "Program.Main",
            ClassName = "Program",
            MethodName = "Main"
        });
        result.CallChains.Add(chain);

        var analyzer = new SimplificationAnalyzer();
        var suggestions = analyzer.Analyze(result);

        suggestions.Should().NotContain(s => s.Target.Contains("Main"));
    }

    [Fact]
    public void Analyze_WithNoIssues_ReturnsEmpty()
    {
        var result = new AnalysisResult();

        var fieldSet = new FieldFlowSet
        {
            FieldName = "_usedField",
            FieldType = "int",
            ClassName = "TestClass"
        };
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
        result.FieldFlowSets.Add(fieldSet);

        var chain1 = new CallChain
        {
            ChainId = 1,
            EntryMethod = "TestClass.Caller"
        };
        chain1.Nodes.Add(new CallChainNode
        {
            MethodFullName = "TestClass.Caller",
            ClassName = "TestClass",
            MethodName = "Caller"
        });
        chain1.Nodes.Add(new CallChainNode
        {
            MethodFullName = "TestClass.MethodA",
            ClassName = "TestClass",
            MethodName = "MethodA"
        });
        chain1.Nodes.Add(new CallChainNode
        {
            MethodFullName = "TestClass.MethodB",
            ClassName = "TestClass",
            MethodName = "MethodB"
        });
        result.CallChains.Add(chain1);

        var analyzer = new SimplificationAnalyzer();
        var suggestions = analyzer.Analyze(result);

        suggestions.Should().NotContain(s => s.Type == SuggestionType.RedundantField);
        suggestions.Should().NotContain(s => s.Target == "TestClass.MethodA");
        suggestions.Should().NotContain(s => s.Target == "TestClass.MethodB");
    }

    [Fact]
    public void Analyze_WithEmptyResult_ReturnsEmpty()
    {
        var result = new AnalysisResult();

        var analyzer = new SimplificationAnalyzer();
        var suggestions = analyzer.Analyze(result);

        suggestions.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_WithMultipleRedundantFields_ReturnsMultipleSuggestions()
    {
        var result = new AnalysisResult();

        for (int i = 1; i <= 3; i++)
        {
            var fieldSet = new FieldFlowSet
            {
                FieldName = $"_unusedField{i}",
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
        }

        var analyzer = new SimplificationAnalyzer();
        var suggestions = analyzer.Analyze(result);

        suggestions.Where(s => s.Type == SuggestionType.RedundantField).Should().HaveCount(3);
    }

    [Fact]
    public void Analyze_WithDisposeMethod_DoesNotMarkAsRedundant()
    {
        var result = new AnalysisResult();
        var chain = new CallChain
        {
            ChainId = 1,
            EntryMethod = "TestClass.Dispose"
        };
        chain.Nodes.Add(new CallChainNode
        {
            MethodFullName = "TestClass.Dispose",
            ClassName = "TestClass",
            MethodName = "Dispose"
        });
        result.CallChains.Add(chain);

        var analyzer = new SimplificationAnalyzer();
        var suggestions = analyzer.Analyze(result);

        suggestions.Should().NotContain(s => s.Target.Contains("Dispose"));
    }

    [Fact]
    public void Analyze_WithInitializeComponent_DoesNotMarkAsRedundant()
    {
        var result = new AnalysisResult();
        var chain = new CallChain
        {
            ChainId = 1,
            EntryMethod = "TestClass.InitializeComponent"
        };
        chain.Nodes.Add(new CallChainNode
        {
            MethodFullName = "TestClass.InitializeComponent",
            ClassName = "TestClass",
            MethodName = "InitializeComponent"
        });
        result.CallChains.Add(chain);

        var analyzer = new SimplificationAnalyzer();
        var suggestions = analyzer.Analyze(result);

        suggestions.Should().NotContain(s => s.Target.Contains("InitializeComponent"));
    }
}
