using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynStringCodeGeneration;

namespace RoslynMethod.Tests.Generation;

public class CodeGenerationTests
{
    [Fact]
    public async Task RoslynGeneratorBase_GenerateAsync_ReturnsSuccessResult()
    {
        var generator = new TestGenerator();
        var model = new StaticStringClassModel
        {
            Fields = new Dictionary<string, string> { { "Greeting", "Hello" } }
        };
        var context = new CodeGenerationContext
        {
            Namespace = "TestNamespace",
            ClassName = "TestClass",
            GenerateAsStatic = true
        };

        var result = await generator.GenerateAsync(model, context);

        result.Success.Should().BeTrue();
        result.GeneratedCode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RoslynGeneratorBase_GenerateAsync_ContainsClassName()
    {
        var generator = new TestGenerator();
        var model = new StaticStringClassModel();
        var context = new CodeGenerationContext
        {
            ClassName = "MyClass"
        };

        var result = await generator.GenerateAsync(model, context);

        result.GeneratedCode.Should().Contain("class MyClass");
    }

    [Fact]
    public async Task RoslynGeneratorBase_GenerateAsync_ContainsNamespace()
    {
        var generator = new TestGenerator();
        var model = new StaticStringClassModel();
        var context = new CodeGenerationContext
        {
            Namespace = "MyNamespace",
            ClassName = "MyClass"
        };

        var result = await generator.GenerateAsync(model, context);

        result.GeneratedCode.Should().Contain("namespace MyNamespace");
    }

    [Fact]
    public async Task StaticStringClassGenerator_WithFields_GeneratesFieldDeclarations()
    {
        var generator = new StaticStringClassGenerator();
        var model = new StaticStringClassModel
        {
            Fields = new Dictionary<string, string>
            {
                { "Name", "Test" },
                { "Value", "123" }
            }
        };
        var context = new CodeGenerationContext
        {
            ClassName = "Constants",
            GenerateAsStatic = true
        };

        var result = await generator.GenerateAsync(model, context);

        result.Success.Should().BeTrue();
        result.GeneratedCode.Should().Contain("Name");
        result.GeneratedCode.Should().Contain("Value");
    }

    [Fact]
    public async Task StaticStringClassGenerator_WithAdditionalUsings_IncludesUsings()
    {
        var generator = new StaticStringClassGenerator();
        var model = new StaticStringClassModel
        {
            Fields = new Dictionary<string, string> { { "Key", "Value" } },
            AdditionalUsings = new List<string> { "System.Collections.Generic" }
        };
        var context = new CodeGenerationContext
        {
            ClassName = "Config"
        };

        var result = await generator.GenerateAsync(model, context);

        result.GeneratedCode.Should().Contain("using System.Collections.Generic");
    }

    [Fact]
    public async Task TemplateGenerator_WithPlaceholders_ReplacesCorrectly()
    {
        var template = "public class {{ClassName}} { }";
        var generator = new TemplateGenerator<TestTemplateModel>(template);
        var model = new TestTemplateModel { ClassName = "GeneratedClass" };
        var context = new CodeGenerationContext();

        var result = await generator.GenerateAsync(model, context);

        result.Success.Should().BeTrue();
        result.GeneratedCode.Should().Contain("class GeneratedClass");
    }

    [Fact]
    public async Task TemplateGenerator_WithCustomProcessor_UsesProcessor()
    {
        var generator = new TemplateGenerator<TestTemplateModel>("",
            (model, ctx) => $"public class {model.ClassName} {{ }}");
        var model = new TestTemplateModel { ClassName = "CustomClass" };
        var context = new CodeGenerationContext();

        var result = await generator.GenerateAsync(model, context);

        result.Success.Should().BeTrue();
        result.GeneratedCode.Should().Contain("class CustomClass");
    }

    [Fact]
    public async Task CompositeGenerator_WithMultipleGenerators_CombinesResults()
    {
        var composite = new CompositeGenerator<StaticStringClassModel>();
        composite.AddGenerator(new StaticStringClassGenerator());

        var model = new StaticStringClassModel
        {
            Fields = new Dictionary<string, string> { { "Field1", "Value1" } }
        };
        var context = new CodeGenerationContext
        {
            ClassName = "Combined"
        };

        var result = await composite.GenerateAsync(model, context);

        result.Success.Should().BeTrue();
        result.GeneratedCode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CodeGenerationContext_WithProperty_StoresProperty()
    {
        var context = new CodeGenerationContext();
        context.WithProperty("Key", 42);

        context.Properties["Key"].Should().Be(42);
    }

    [Fact]
    public void CodeGenerationResult_ErrorResult_ReturnsError()
    {
        var result = CodeGenerationResult.ErrorResult(new InvalidOperationException("Test error"));

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error.Message.Should().Be("Test error");
    }

    [Fact]
    public void CodeGenerationResult_SuccessResult_ReturnsSuccess()
    {
        var result = CodeGenerationResult.SuccessResult("test code");

        result.Success.Should().BeTrue();
        result.GeneratedCode.Should().Be("test code");
    }

    [Fact]
    public void RoslynGeneratorBase_CreateField_WithStringType_ReturnsFieldDeclaration()
    {
        var generator = new TestGenerator();

        var field = generator.CreateFieldPublic("string", "_myField", "initialValue");

        field.Should().NotBeNull();
        field.ToString().Should().Contain("_myField");
    }

    [Fact]
    public void RoslynGeneratorBase_CreateField_WithNonStringType_ThrowsNotSupportedException()
    {
        var generator = new TestGenerator();

        var action = () => generator.CreateFieldPublic("int", "_number", "0");

        action.Should().Throw<NotSupportedException>();
    }
}

public class TestGenerator : RoslynGeneratorBase<StaticStringClassModel>
{
    protected override Task<CompilationUnitSyntax> BuildCompilationUnitAsync(StaticStringClassModel model, CodeGenerationContext context)
    {
        var fieldDeclarations = model.Fields.Select(kvp =>
            CreateField("string", kvp.Key, kvp.Value,
                isStatic: context.GenerateAsStatic,
                isReadOnly: context.GenerateAsReadOnly)
        ).Cast<MemberDeclarationSyntax>().ToList();

        var classDeclaration = CreateClass(context.ClassName, SyntaxFactory.List(fieldDeclarations), isStatic: context.GenerateAsStatic);

        MemberDeclarationSyntax member = classDeclaration;
        if (!string.IsNullOrEmpty(context.Namespace))
            member = CreateNamespace(context.Namespace, SyntaxFactory.SingletonList<MemberDeclarationSyntax>(classDeclaration));

        var usings = new List<UsingDirectiveSyntax>();
        usings.Add(CreateUsing("System"));
        usings.AddRange(model.AdditionalUsings.Select(CreateUsing));

        var compilationUnit = SyntaxFactory.CompilationUnit()
            .WithUsings(SyntaxFactory.List(usings))
            .WithMembers(SyntaxFactory.SingletonList(member));

        return Task.FromResult(compilationUnit);
    }

    public FieldDeclarationSyntax CreateFieldPublic(string typeName, string fieldName, string initializerValue, bool isStatic = true, bool isReadOnly = true)
        => CreateField(typeName, fieldName, initializerValue, isStatic, isReadOnly);
}

public class TestTemplateModel
{
    public string ClassName { get; set; }
}
