using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynSpace.RoslynStringCodeGeneration;

public abstract class RoslynGeneratorBase<TModel>
{
    public async Task<CodeGenerationResult> GenerateAsync(TModel model, CodeGenerationContext context)
    {
        try
        {
            var compilationUnit = await BuildCompilationUnitAsync(model, context);
            var code = compilationUnit.NormalizeWhitespace().ToFullString();
            return CodeGenerationResult.SuccessResult(code);
        }
        catch (Exception ex)
        {
            return CodeGenerationResult.ErrorResult(ex);
        }
    }

    protected abstract Task<CompilationUnitSyntax> BuildCompilationUnitAsync(TModel model, CodeGenerationContext context);

    protected FieldDeclarationSyntax CreateField(string typeName, string fieldName, string initializerValue,
        bool isStatic = true, bool isReadOnly = true)
    {
        var variableDeclarator = SyntaxFactory.VariableDeclarator(fieldName)
            .WithInitializer(SyntaxFactory.EqualsValueClause(
                SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(initializerValue))));

        var variableDeclaration = SyntaxFactory.VariableDeclaration(
            SyntaxFactory.ParseTypeName(typeName))
            .WithVariables(SyntaxFactory.SingletonSeparatedList(variableDeclarator));

        var fieldDeclaration = SyntaxFactory.FieldDeclaration(variableDeclaration);

        if (isStatic)
            fieldDeclaration = fieldDeclaration.AddModifiers(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
        if (isReadOnly)
            fieldDeclaration = fieldDeclaration.AddModifiers(SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword));

        return fieldDeclaration;
    }

    protected ClassDeclarationSyntax CreateClass(string className, SyntaxList<MemberDeclarationSyntax> members,
        bool isStatic = false)
    {
        var classDeclaration = SyntaxFactory.ClassDeclaration(className)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithMembers(members);

        if (isStatic)
            classDeclaration = classDeclaration.AddModifiers(SyntaxFactory.Token(SyntaxKind.StaticKeyword));

        return classDeclaration;
    }

    protected NamespaceDeclarationSyntax CreateNamespace(string name, SyntaxList<MemberDeclarationSyntax> members)
    {
        return SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(name))
            .WithMembers(members);
    }

    protected UsingDirectiveSyntax CreateUsing(string namespaceName)
    {
        return SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName));
    }
}
