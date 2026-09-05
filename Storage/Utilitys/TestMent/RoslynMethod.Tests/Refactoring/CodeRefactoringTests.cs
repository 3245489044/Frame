using RoslynSpace.RoslynStringRefactoringFramework;

namespace RoslynMethod.Tests.Refactoring;

public class CodeRefactoringTests
{
    [Fact]
    public async Task RoslynRefactoringFramework_RefactorAsync_WithSimpleCode_ReturnsSuccess()
    {
        var framework = new RoslynRefactoringFramework();
        var context = new CodeRefactoringContext("public class Test { }");

        var result = await framework.RefactorAsync(context);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RoslynRefactoringFramework_RefactorAsync_WithUseVarRule_TransformsCode()
    {
        var framework = new RoslynRefactoringFramework();
        framework.AddRule(new UseVarKeywordRule());
        var code = "public class Test { public void M() { string x = \"hello\"; } }";
        var context = new CodeRefactoringContext(code);

        var result = await framework.RefactorAsync(context);

        result.Success.Should().BeTrue();
        result.AppliedRefactorings.Should().Contain("UseVarKeyword");
    }

    [Fact]
    public async Task RoslynRefactoringFramework_RefactorAsync_WithSimplifyLinqRule_TransformsCode()
    {
        var framework = new RoslynRefactoringFramework();
        framework.AddRule(new SimplifyLinqRule());
        var code = "using System.Linq; public class Test { public void M() { var items = new[] { 1, 2, 3 }.Where(x => x > 1).FirstOrDefault(); } }";
        var context = new CodeRefactoringContext(code);

        var result = await framework.RefactorAsync(context);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RoslynRefactoringFramework_RefactorAsync_WithAddNullChecksRule_TransformsCode()
    {
        var framework = new RoslynRefactoringFramework();
        framework.AddRule(new AddNullChecksRule());
        var code = "using System; public class Test { public void M(string input) { Console.WriteLine(input); } }";
        var context = new CodeRefactoringContext(code);

        var result = await framework.RefactorAsync(context);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RoslynRefactoringFramework_RefactorAsync_WithNoRules_ReturnsOriginalCode()
    {
        var framework = new RoslynRefactoringFramework();
        var code = "public class Test { }";
        var context = new CodeRefactoringContext(code);

        var result = await framework.RefactorAsync(context);

        result.Success.Should().BeTrue();
        result.TransformedCode.Should().Contain("class Test");
    }

    [Fact]
    public async Task RoslynRefactoringFramework_RefactorAsync_WithDangerousCode_ReturnsError()
    {
        var framework = new RoslynRefactoringFramework();
        var code = "public class Test { public void M() { System.IO.File.Delete(\"test.txt\"); } }";
        var context = new CodeRefactoringContext(code);

        var result = await framework.RefactorAsync(context);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RoslynRefactoringFramework_RefactorAsync_WithMultipleRules_AppliesAll()
    {
        var framework = new RoslynRefactoringFramework();
        framework.AddRule(new UseVarKeywordRule());
        framework.AddRule(new SimplifyLinqRule());
        var code = "using System.Linq; public class Test { public void M() { string x = \"hello\"; var items = new[] { 1, 2, 3 }.Where(i => i > 1).FirstOrDefault(); } }";
        var context = new CodeRefactoringContext(code);

        var result = await framework.RefactorAsync(context);

        result.Success.Should().BeTrue();
        result.AppliedRefactorings.Should().NotBeEmpty();
    }

    [Fact]
    public void CodeRefactoringContext_Constructor_SetsSourceCode()
    {
        var context = new CodeRefactoringContext("test code");

        context.SourceCode.Should().Be("test code");
        context.ProjectName.Should().Be("TempProject");
        context.References.Should().NotBeEmpty();
    }

    [Fact]
    public void CodeRefactoringContext_Constructor_WithNull_ThrowsArgumentNullException()
    {
        var action = () => new CodeRefactoringContext(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RefactoringResult_SuccessResult_ReturnsSuccess()
    {
        var result = RefactoringResult.SuccessResult("transformed", new List<string> { "Rule1" });

        result.Success.Should().BeTrue();
        result.TransformedCode.Should().Be("transformed");
        result.AppliedRefactorings.Should().Contain("Rule1");
    }

    [Fact]
    public void RefactoringResult_ErrorResult_ReturnsError()
    {
        var result = RefactoringResult.ErrorResult(new InvalidOperationException("Error"));

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public void UseVarKeywordRule_Name_IsCorrect()
    {
        var rule = new UseVarKeywordRule();

        rule.Name.Should().Be("UseVarKeyword");
    }

    [Fact]
    public void SimplifyLinqRule_Name_IsCorrect()
    {
        var rule = new SimplifyLinqRule();

        rule.Name.Should().Be("SimplifyLinq");
    }

    [Fact]
    public void AddNullChecksRule_Name_IsCorrect()
    {
        var rule = new AddNullChecksRule();

        rule.Name.Should().Be("AddNullChecks");
    }

    [Fact]
    public void RoslynCacheManager_ClearExpiredCache_DoesNotThrow()
    {
        var manager = new RoslynCacheManager();

        var action = () => manager.ClearExpiredCache();

        action.Should().NotThrow();
    }
}
