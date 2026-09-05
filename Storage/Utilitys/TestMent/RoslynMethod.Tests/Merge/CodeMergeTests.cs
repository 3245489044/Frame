using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynSpace.RoslynCompilationIntegration;

namespace RoslynMethod.Tests.Merge;

public class CodeMergeTests
{
    private const string OriginalCode = @"
using System;
namespace TestNamespace
{
    public class TestClass
    {
        public int Add(int a, int b)
        {
            return a + b;
        }

        public void Print(string message)
        {
            Console.WriteLine(message);
        }
    }
}";

    private const string ModifiedCode = @"
using System;
namespace TestNamespace
{
    public class TestClass
    {
        public int Add(int a, int b)
        {
            int result = a + b;
            return result;
        }

        public void Print(string message)
        {
            Console.WriteLine(""[Modified] "" + message);
        }

        public void NewMethod()
        {
            Console.WriteLine(""New method added"");
        }
    }
}";

    [Fact]
    public void MergeMethodsIntelligently_WithIdenticalTrees_ReturnsNoChanges()
    {
        var originalTree = CSharpSyntaxTree.ParseText(OriginalCode);
        var modifiedTree = CSharpSyntaxTree.ParseText(OriginalCode);

        var result = EnhancedMethodMergeEngine.MergeMethodsIntelligently(
            originalTree, modifiedTree, EnhancedMethodMergeEngine.MergeStrategy.Intelligent);

        result.Success.Should().BeTrue();
        result.HasConflicts.Should().BeFalse();
    }

    [Fact]
    public void MergeMethodsIntelligently_WithModifiedCode_ReturnsMergedResult()
    {
        var originalTree = CSharpSyntaxTree.ParseText(OriginalCode);
        var modifiedTree = CSharpSyntaxTree.ParseText(ModifiedCode);

        var result = EnhancedMethodMergeEngine.MergeMethodsIntelligently(
            originalTree, modifiedTree, EnhancedMethodMergeEngine.MergeStrategy.Intelligent);

        result.Success.Should().BeTrue();
        result.MergedCode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void MergeMethodsIntelligently_WithNullOriginal_ThrowsArgumentNullException()
    {
        var modifiedTree = CSharpSyntaxTree.ParseText(ModifiedCode);

        var action = () => EnhancedMethodMergeEngine.MergeMethodsIntelligently(
            null!, modifiedTree);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MergeMethodsIntelligently_WithNullModified_ThrowsArgumentNullException()
    {
        var originalTree = CSharpSyntaxTree.ParseText(OriginalCode);

        var action = () => EnhancedMethodMergeEngine.MergeMethodsIntelligently(
            originalTree, null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MergeMethodsIntelligently_WithAddedMethod_ReturnsAddedStatus()
    {
        var originalTree = CSharpSyntaxTree.ParseText(OriginalCode);
        var modifiedTree = CSharpSyntaxTree.ParseText(ModifiedCode);

        var result = EnhancedMethodMergeEngine.MergeMethodsIntelligently(
            originalTree, modifiedTree);

        result.MethodDetails.Should().Contain(m => m.Status == EnhancedMethodMergeEngine.MergeStatus.Added);
    }

    [Fact]
    public void MergeMethodsIntelligently_WithConservativeStrategy_ReturnsResult()
    {
        var originalTree = CSharpSyntaxTree.ParseText(OriginalCode);
        var modifiedTree = CSharpSyntaxTree.ParseText(ModifiedCode);

        var result = EnhancedMethodMergeEngine.MergeMethodsIntelligently(
            originalTree, modifiedTree, EnhancedMethodMergeEngine.MergeStrategy.Conservative);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void MergeMethodsIntelligently_WithAggressiveStrategy_ReturnsResult()
    {
        var originalTree = CSharpSyntaxTree.ParseText(OriginalCode);
        var modifiedTree = CSharpSyntaxTree.ParseText(ModifiedCode);

        var result = EnhancedMethodMergeEngine.MergeMethodsIntelligently(
            originalTree, modifiedTree, EnhancedMethodMergeEngine.MergeStrategy.Aggressive);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void MergeMethodsIntelligently_WithThreeWayStrategy_ReturnsResult()
    {
        var originalTree = CSharpSyntaxTree.ParseText(OriginalCode);
        var modifiedTree = CSharpSyntaxTree.ParseText(ModifiedCode);

        var result = EnhancedMethodMergeEngine.MergeMethodsIntelligently(
            originalTree, modifiedTree, EnhancedMethodMergeEngine.MergeStrategy.ThreeWay);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void StatementSimilarityThreshold_SetAndGet_WorksCorrectly()
    {
        EnhancedMethodMergeEngine.StatementSimilarityThreshold = 0.9;

        var threshold = EnhancedMethodMergeEngine.StatementSimilarityThreshold;

        threshold.Should().Be(0.9);
    }

    [Fact]
    public void StatementSimilarityThreshold_SetAboveOne_ClampsToOne()
    {
        EnhancedMethodMergeEngine.StatementSimilarityThreshold = 1.5;

        var threshold = EnhancedMethodMergeEngine.StatementSimilarityThreshold;

        threshold.Should().Be(1.0);
    }

    [Fact]
    public void StatementSimilarityThreshold_SetBelowZero_ClampsToZero()
    {
        EnhancedMethodMergeEngine.StatementSimilarityThreshold = -0.5;

        var threshold = EnhancedMethodMergeEngine.StatementSimilarityThreshold;

        threshold.Should().Be(0.0);
    }

    [Fact]
    public void BlockSimilarityThreshold_SetAndGet_WorksCorrectly()
    {
        EnhancedMethodMergeEngine.BlockSimilarityThreshold = 0.8;

        var threshold = EnhancedMethodMergeEngine.BlockSimilarityThreshold;

        threshold.Should().Be(0.8);
    }

    [Fact]
    public void EnhancedMergeResult_DefaultValues_AreCorrect()
    {
        var result = new EnhancedMergeResult();

        result.Success.Should().BeFalse();
        result.HasConflicts.Should().BeFalse();
        result.TotalConflicts.Should().Be(0);
        result.MethodDetails.Should().BeNull();
    }
}
