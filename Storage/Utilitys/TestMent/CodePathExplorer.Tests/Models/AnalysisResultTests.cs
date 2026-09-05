using CodePathExplorer.Models;

namespace CodePathExplorer.Tests.Models;

public class AnalysisResultTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var result = new AnalysisResult();

        result.SourcePath.Should().Be("");
        result.FileCount.Should().Be(0);
        result.ClassCount.Should().Be(0);
        result.CallChains.Should().NotBeNull().And.BeEmpty();
        result.FieldFlowSets.Should().NotBeNull().And.BeEmpty();
        result.Suggestions.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Properties_SetAndGet_Correctly()
    {
        var result = new AnalysisResult
        {
            SourcePath = @"C:\Test",
            FileCount = 10,
            ClassCount = 5
        };

        result.SourcePath.Should().Be(@"C:\Test");
        result.FileCount.Should().Be(10);
        result.ClassCount.Should().Be(5);
    }

    [Fact]
    public void CallChains_AddItems_Works()
    {
        var result = new AnalysisResult();
        result.CallChains.Add(new CallChain { ChainId = 1, EntryMethod = "Test.Method" });

        result.CallChains.Should().HaveCount(1);
        result.CallChains[0].ChainId.Should().Be(1);
    }

    [Fact]
    public void FieldFlowSets_AddItems_Works()
    {
        var result = new AnalysisResult();
        result.FieldFlowSets.Add(new FieldFlowSet { FieldName = "testField", ClassName = "TestClass" });

        result.FieldFlowSets.Should().HaveCount(1);
        result.FieldFlowSets[0].FieldName.Should().Be("testField");
    }

    [Fact]
    public void Suggestions_AddItems_Works()
    {
        var result = new AnalysisResult();
        result.Suggestions.Add(new SimplificationSuggestion
        {
            Type = SuggestionType.RedundantField,
            Target = "field1",
            Description = "Test suggestion"
        });

        result.Suggestions.Should().HaveCount(1);
        result.Suggestions[0].Type.Should().Be(SuggestionType.RedundantField);
    }
}

public class SimplificationSuggestionTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var suggestion = new SimplificationSuggestion();

        suggestion.Type.Should().Be(SuggestionType.RedundantField);
        suggestion.Description.Should().Be("");
        suggestion.Target.Should().Be("");
        suggestion.ClassName.Should().Be("");
        suggestion.Detail.Should().Be("");
    }

    [Fact]
    public void Properties_SetAndGet_Correctly()
    {
        var suggestion = new SimplificationSuggestion
        {
            Type = SuggestionType.InternalizableField,
            Description = "Field can be internalized",
            Target = "myField",
            ClassName = "MyClass",
            Detail = "Detail info"
        };

        suggestion.Type.Should().Be(SuggestionType.InternalizableField);
        suggestion.Description.Should().Be("Field can be internalized");
        suggestion.Target.Should().Be("myField");
        suggestion.ClassName.Should().Be("MyClass");
        suggestion.Detail.Should().Be("Detail info");
    }
}

public class SuggestionTypeTests
{
    [Fact]
    public void SuggestionType_Values_AreDefined()
    {
        Enum.GetValues<SuggestionType>().Should().HaveCount(4);
        Enum.GetName(SuggestionType.RedundantField).Should().Be("RedundantField");
        Enum.GetName(SuggestionType.InternalizableField).Should().Be("InternalizableField");
        Enum.GetName(SuggestionType.RedundantMethod).Should().Be("RedundantMethod");
        Enum.GetName(SuggestionType.Simplifiable).Should().Be("Simplifiable");
    }
}
