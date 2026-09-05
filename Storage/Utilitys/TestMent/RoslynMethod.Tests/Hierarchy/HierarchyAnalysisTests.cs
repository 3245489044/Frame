using RoslynSpace.RoslynSyntaxHierarchy;

namespace RoslynMethod.Tests.Hierarchy;

public class HierarchyAnalysisTests
{
    [Fact]
    public void LevelStatistics_DefaultValues_AreZero()
    {
        var stats = new LevelStatistics();

        stats.FunctionCount.Should().Be(0);
        stats.AverageCallers.Should().Be(0);
        stats.HasCircularDependency.Should().BeFalse();
        stats.TopLevelFunctions.Should().Be(0);
        stats.LeafFunctions.Should().Be(0);
    }

    [Fact]
    public void FunctionHierarchyInfo_DefaultValues_AreCorrect()
    {
        var info = new FunctionHierarchyInfo();

        info.FunctionSignature.Should().BeNull();
        info.FunctionName.Should().BeNull();
        info.Level.Should().Be(0);
        info.IsTopLevel.Should().BeFalse();
        info.IsLeafFunction.Should().BeFalse();
        info.Parameters.Should().BeEmpty();
        info.CalledFunctions.Should().BeEmpty();
        info.Callers.Should().BeEmpty();
    }

    [Fact]
    public void CallGraphData_DefaultValues_AreCorrect()
    {
        var data = new CallGraphData();

        data.CallGraph.Should().BeEmpty();
        data.ReverseGraph.Should().BeEmpty();
        data.MethodSignatures.Should().BeEmpty();
    }

    [Fact]
    public void MethodData_DefaultValues_AreCorrect()
    {
        var data = new MethodData();

        data.Method.Should().BeNull();
        data.Symbol.Should().BeNull();
        data.Signature.Should().BeNull();
        data.MethodName.Should().BeNull();
        data.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void HierarchyExport_DefaultValues_AreCorrect()
    {
        var export = new HierarchyExport();

        export.Content.Should().BeNull();
        export.Format.Should().BeNull();
    }

    [Fact]
    public void FunctionGraphVisualizer_Export_WithDotFormat_ReturnsDotContent()
    {
        var hierarchy = new List<FunctionHierarchyInfo>
        {
            new()
            {
                FunctionSignature = "TestClass.Method1",
                FunctionName = "Method1",
                Level = 1,
                IsTopLevel = true,
                CalledFunctions = new List<string> { "TestClass.Method2" }
            },
            new()
            {
                FunctionSignature = "TestClass.Method2",
                FunctionName = "Method2",
                Level = 2,
                IsLeafFunction = true
            }
        };

        var export = FunctionGraphVisualizer.Export(hierarchy, "dot");

        export.Format.Should().Be("dot");
        export.Content.Should().Contain("digraph");
    }

    [Fact]
    public void FunctionGraphVisualizer_Export_WithJsonFormat_ReturnsJsonContent()
    {
        var hierarchy = new List<FunctionHierarchyInfo>
        {
            new()
            {
                FunctionSignature = "TestClass.Method1",
                FunctionName = "Method1",
                Level = 1,
                IsTopLevel = true
            }
        };

        var export = FunctionGraphVisualizer.Export(hierarchy, "json");

        export.Format.Should().Be("json");
        export.Content.Should().Contain("nodes");
    }

    [Fact]
    public void FunctionGraphVisualizer_Export_WithInvalidFormat_ThrowsArgumentException()
    {
        var hierarchy = new List<FunctionHierarchyInfo>
        {
            new() { FunctionSignature = "M", FunctionName = "M" }
        };

        var action = () => FunctionGraphVisualizer.Export(hierarchy, "invalid");

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FunctionGraphVisualizer_ToDotGraph_WithEmptyHierarchy_ReturnsValidDot()
    {
        var dot = FunctionGraphVisualizer.ToDotGraph(new List<FunctionHierarchyInfo>());

        dot.Should().Contain("digraph");
    }

    [Fact]
    public void FunctionGraphVisualizer_ToJson_WithEmptyHierarchy_ReturnsValidJson()
    {
        var action = () => FunctionGraphVisualizer.ToJson(new List<FunctionHierarchyInfo>());

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetLevelStatistics_WithEmptyHierarchy_ReturnsDefaultStats()
    {
        var stats = FunctionHierarchyAnalyzer.GetLevelStatistics(new List<FunctionHierarchyInfo>());

        stats.FunctionCount.Should().Be(0);
    }

    [Fact]
    public void FindCircularDependencies_WithNoCycles_ReturnsEmpty()
    {
        var hierarchy = new List<FunctionHierarchyInfo>
        {
            new()
            {
                FunctionSignature = "A",
                CalledFunctions = new List<string> { "B" }
            },
            new()
            {
                FunctionSignature = "B",
                CalledFunctions = new List<string>()
            }
        };

        var cycles = hierarchy.FindCircularDependencies();

        cycles.Should().BeEmpty();
    }
}
