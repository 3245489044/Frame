using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace RoslynMethod.Tests.Core;

public class AnalysisContextTests
{
    private const string SimpleClassCode = @"
class TestClass
{
    public void Method1() { }
    private int Method2() { return 0; }
    public string Property1 { get; set; }
    private int _field1;
}
class Program { public static void Main() { } }";

    private const string CodeWithNamespace = @"
namespace MyNamespace
{
    class MyClass
    {
        public void DoSomething() { }
    }
    class Program { public static void Main() { } }
}";

    [Fact]
    public void Create_WithValidCode_ReturnsContext()
    {
        var result = RoslynAnalyzer.TryInitialize(SimpleClassCode, out var context);

        result.Should().BeTrue();
        context.Should().NotBeNull();
        context!.SemanticModel.Should().NotBeNull();
    }

    [Fact]
    public void Create_WithEmptyCode_ReturnsFalse()
    {
        var result = RoslynAnalyzer.TryInitialize("", out var context);

        result.Should().BeFalse();
        context.Should().BeNull();
    }

    [Fact]
    public void Create_WithNullCode_ReturnsFalse()
    {
        var result = RoslynAnalyzer.TryInitialize(null!, out var context);

        result.Should().BeFalse();
        context.Should().BeNull();
    }

    [Fact]
    public void Create_WithWhitespaceCode_ReturnsFalse()
    {
        var result = RoslynAnalyzer.TryInitialize("   ", out var context);

        result.Should().BeFalse();
        context.Should().BeNull();
    }

    [Fact]
    public void GetSemanticModel_WithIsCurrentTrue_ReturnsCurrentSemanticModel()
    {
        var context = AnalysisContext.Create(SimpleClassCode);

        var model = context.GetSemanticModel(true);

        model.Should().NotBeNull();
    }

    [Fact]
    public void HasCompilationErrors_WithValidCode_ReturnsFalse()
    {
        var context = AnalysisContext.Create(SimpleClassCode);

        context.HasCompilationErrors().Should().BeFalse();
    }

    [Fact]
    public void HasCompilationErrors_WithInvalidCode_ReturnsTrue()
    {
        const string invalidCode = "class { void M() { }";

        var context = AnalysisContext.Create(invalidCode);

        context.HasCompilationErrors().Should().BeTrue();
    }

    [Fact]
    public void ValidateOrThrow_WithValidCode_DoesNotThrow()
    {
        var context = AnalysisContext.Create(SimpleClassCode);

        var action = () => context.ValidateOrThrow();

        action.Should().NotThrow();
    }

    [Fact]
    public void ValidateOrThrow_WithInvalidCode_ThrowsInvalidOperationException()
    {
        const string invalidCode = "class { void M() { }";

        var context = AnalysisContext.Create(invalidCode);

        var action = () => context.ValidateOrThrow();

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetDiagnostics_WithValidCode_ReturnsEmpty()
    {
        var context = AnalysisContext.Create(SimpleClassCode);

        var diagnostics = context.GetDiagnostics();

        diagnostics.Should().NotBeNull();
    }

    [Fact]
    public void Dispose_ReleasesResources()
    {
        var context = AnalysisContext.Create(SimpleClassCode);

        context.Dispose();

        var action = () => context.Dispose();
        action.Should().NotThrow();
    }

    [Fact]
    public void Create_WithNamespace_ReturnsContext()
    {
        var context = AnalysisContext.Create(CodeWithNamespace);

        context.Should().NotBeNull();
        context.SemanticModel.Should().NotBeNull();
    }

    [Fact]
    public async Task InitializeAsync_WithValidCode_ReturnsContext()
    {
        var context = await RoslynAnalyzer.InitializeAsync(SimpleClassCode);

        context.Should().NotBeNull();
        context.SemanticModel.Should().NotBeNull();
    }

    [Fact]
    public void GetSourceText_ReturnsOriginalCode()
    {
        var context = AnalysisContext.Create(SimpleClassCode);

        var sourceText = context.GetSourceText();

        sourceText.Should().Contain("class TestClass");
        sourceText.Should().Contain("Method1");
    }

    [Fact]
    public void GetFormattedCode_ReturnsNormalizedCode()
    {
        var context = AnalysisContext.Create(SimpleClassCode);

        var formattedCode = context.GetFormattedCode();

        formattedCode.Should().NotBeNullOrEmpty();
        formattedCode.Should().Contain("TestClass");
    }
}
