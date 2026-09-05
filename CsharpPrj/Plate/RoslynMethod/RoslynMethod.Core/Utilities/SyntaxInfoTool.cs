using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynSpace.RoslynSyntaxUtilities.Base;

public static class SyntaxInfoTool
{
    public static string GetAccessibilityString(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(SyntaxKind.PublicKeyword)) return "public";
        if (modifiers.Any(SyntaxKind.PrivateKeyword)) return "private";
        if (modifiers.Any(SyntaxKind.ProtectedKeyword)) return "protected";
        if (modifiers.Any(SyntaxKind.InternalKeyword)) return "internal";
        return "internal";
    }

    public static SeparatedSyntaxList<ParameterSyntax> GetMethodParameters(MethodDeclarationSyntax method)
    {
        return method.ParameterList.Parameters;
    }

    public static TypeSyntax GetMethodReturnType(MethodDeclarationSyntax method)
    {
        return method.ReturnType;
    }

    public static bool IsVoidReturnType(TypeSyntax returnType)
    {
        return returnType is PredefinedTypeSyntax predefinedType &&
               predefinedType.Keyword.IsKind(SyntaxKind.VoidKeyword);
    }

    public static (int line, int column) GetPosition(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return (span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
    }

    public static string GetFullTypeName(BaseTypeDeclarationSyntax typeDecl)
    {
        var name = typeDecl.Identifier.Text;
        var parent = typeDecl.Parent;
        while (parent is BaseTypeDeclarationSyntax parentType)
        {
            name = parentType.Identifier.Text + "." + name;
            parent = parent.Parent;
        }
        if (parent is NamespaceDeclarationSyntax ns)
        {
            name = ns.Name.ToString() + "." + name;
        }
        return name;
    }

    public static bool HasReturnStatement(BlockSyntax body)
    {
        return body.DescendantNodes().OfType<ReturnStatementSyntax>().Any();
    }
}
