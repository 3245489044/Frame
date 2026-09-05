using CodePathExplorer.Scanner;

namespace CodePathExplorer.Tests.Scanner;

public class CodebaseScannerTests
{
    private readonly string _tempDir;

    public CodebaseScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CodePathExplorerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    protected virtual void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
            }
        }
    }

    private string CreateTestFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    [Fact]
    public void Scan_WithValidDirectory_ReturnsScanResult()
    {
        CreateTestFile("TestClass.cs", @"
class TestClass
{
    public void Method() { }
}");
        var scanner = new CodebaseScanner();

        var result = scanner.Scan(_tempDir);

        result.Should().NotBeNull();
        result.TotalFiles.Should().Be(1);
        result.ParsedFiles.Should().Be(1);
        result.DirectoryPath.Should().Be(_tempDir);
    }

    [Fact]
    public void Scan_WithMultipleFiles_ReturnsCorrectCount()
    {
        CreateTestFile("ClassA.cs", "class ClassA { public void A() {} }");
        CreateTestFile("ClassB.cs", "class ClassB { public void B() {} }");
        CreateTestFile("ClassC.cs", "class ClassC { public void C() {} }");
        var scanner = new CodebaseScanner();

        var result = scanner.Scan(_tempDir);

        result.TotalFiles.Should().Be(3);
        result.ParsedFiles.Should().Be(3);
    }

    [Fact]
    public void Scan_WithInvalidCode_ReportsError()
    {
        CreateTestFile("Invalid.cs", "");
        var scanner = new CodebaseScanner();

        var result = scanner.Scan(_tempDir);

        result.TotalFiles.Should().Be(1);
        result.ParsedFiles.Should().Be(1);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Scan_WithEmptyDirectory_ReturnsZeroFiles()
    {
        var scanner = new CodebaseScanner();

        var result = scanner.Scan(_tempDir);

        result.TotalFiles.Should().Be(0);
        result.ParsedFiles.Should().Be(0);
        result.Contexts.Should().BeEmpty();
    }

    [Fact]
    public void Scan_WithNonExistentDirectory_ThrowsException()
    {
        var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistentDir_" + Guid.NewGuid());
        var scanner = new CodebaseScanner();

        var action = () => scanner.Scan(invalidPath);

        action.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Scan_WithSubdirectories_IncludesFilesInSubdirs()
    {
        CreateTestFile("SubDir/ClassA.cs", "class ClassA { public void A() {} }");
        CreateTestFile("Nested/Deep/ClassB.cs", "class ClassB { public void B() {} }");
        var scanner = new CodebaseScanner();

        var result = scanner.Scan(_tempDir);

        result.TotalFiles.Should().Be(2);
        result.ParsedFiles.Should().Be(2);
    }

    [Fact]
    public void Scan_ExcludesObjDirectory()
    {
        CreateTestFile("obj/Debug/Generated.cs", "class Generated { public void Temp() {} }");
        CreateTestFile("RealClass.cs", "class RealClass { public void Method() {} }");
        var scanner = new CodebaseScanner();

        var result = scanner.Scan(_tempDir);

        result.TotalFiles.Should().Be(1);
        result.ParsedFiles.Should().Be(1);
    }

    [Fact]
    public void Scan_ExcludesBinDirectory()
    {
        CreateTestFile("bin/Release/Built.cs", "class Built { public void Temp() {} }");
        CreateTestFile("Source.cs", "class Source { public void Method() {} }");
        var scanner = new CodebaseScanner();

        var result = scanner.Scan(_tempDir);

        result.TotalFiles.Should().Be(1);
        result.ParsedFiles.Should().Be(1);
    }

    [Fact]
    public void Scan_ExcludesNodeModulesDirectory()
    {
        CreateTestFile("node_modules/package/lib.cs", "class Lib { public void Helper() {} }");
        CreateTestFile("App.cs", "class App { public void Run() {} }");
        var scanner = new CodebaseScanner();

        var result = scanner.Scan(_tempDir);

        result.TotalFiles.Should().Be(1);
        result.ParsedFiles.Should().Be(1);
    }

    [Fact]
    public void Scan_ExcludesGitDirectory()
    {
        CreateTestFile(".git/objects/abc123.cs", "class Temp { }");
        CreateTestFile("Project.cs", "class Project { public void Start() {} }");
        var scanner = new CodebaseScanner();

        var result = scanner.Scan(_tempDir);

        result.TotalFiles.Should().Be(1);
        result.ParsedFiles.Should().Be(1);
    }

    [Fact]
    public void Scan_WithValidCode_ContextHasCorrectProperties()
    {
        const string code = @"
using System;
namespace MyApp
{
    class MyClass
    {
        public void DoSomething() { }
    }
}";
        CreateTestFile("MyClass.cs", code);
        var scanner = new CodebaseScanner();

        var result = scanner.Scan(_tempDir);

        result.Contexts.Should().HaveCount(1);
        var context = result.Contexts[0];
        context.FilePath.Should().NotBeNullOrEmpty();
        context.SourceCode.Should().Be(code);
        context.SyntaxTree.Should().NotBeNull();
        context.Root.Should().NotBeNull();
        context.SemanticModel.Should().NotBeNull();
    }

    [Fact]
    public void Scan_WithMixedValidAndInvalid_FiltersCorrectly()
    {
        CreateTestFile("Valid.cs", "class Valid { public void M() {} }");
        CreateTestFile("Invalid.cs", "");
        CreateTestFile("obj/Temp.cs", "class Temp { }");
        var scanner = new CodebaseScanner();

        var result = scanner.Scan(_tempDir);

        result.TotalFiles.Should().Be(2);
        result.ParsedFiles.Should().Be(2);
        result.Errors.Should().BeEmpty();
    }
}
