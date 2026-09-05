using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynSpace.RoslynStringCodeGeneration;

public class StaticStringClassGenerator : RoslynGeneratorBase<StaticStringClassModel>
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

        var usings = new List<UsingDirectiveSyntax>
        {
            CreateUsing("System")
        };
        usings.AddRange(model.AdditionalUsings.Select(CreateUsing));

        var compilationUnit = SyntaxFactory.CompilationUnit()
            .WithUsings(SyntaxFactory.List(usings))
            .WithMembers(SyntaxFactory.SingletonList(member));

        return Task.FromResult(compilationUnit);
    }
}

public class TemplateGenerator<TModel> : RoslynGeneratorBase<TModel>
{
    private readonly string _template;
    private readonly Func<TModel, CodeGenerationContext, string>? _customProcessor;

    public TemplateGenerator(string template)
    {
        _template = template;
    }

    public TemplateGenerator(string template, Func<TModel, CodeGenerationContext, string> customProcessor)
    {
        _template = template;
        _customProcessor = customProcessor;
    }

    protected override Task<CompilationUnitSyntax> BuildCompilationUnitAsync(TModel model, CodeGenerationContext context)
    {
        string code;
        if (_customProcessor != null)
        {
            code = _customProcessor(model, context);
        }
        else
        {
            code = _template.Replace("{{ClassName}}", context.ClassName);
        }

        var compilationUnit = SyntaxFactory.CompilationUnit()
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(
                SyntaxFactory.ParseMemberDeclaration(code)!));

        return Task.FromResult(compilationUnit);
    }
}

public class CompositeGenerator<TModel>
{
    private readonly List<RoslynGeneratorBase<TModel>> _generators = new();

    public void AddGenerator(RoslynGeneratorBase<TModel> generator)
    {
        _generators.Add(generator);
    }

    public async Task<CodeGenerationResult> GenerateAsync(TModel model, CodeGenerationContext context)
    {
        var combinedCode = new System.Text.StringBuilder();
        foreach (var generator in _generators)
        {
            var result = await generator.GenerateAsync(model, context);
            if (!result.Success)
                return result;
            combinedCode.AppendLine(result.GeneratedCode);
        }
        return CodeGenerationResult.SuccessResult(combinedCode.ToString());
    }
}
