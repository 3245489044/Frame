using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

namespace RoslynSpace.RoslynSyntaxUtilities.Generation
{
    public class MethodCommentProvider : ICommentProvider
    {
        public string GenerateComment(SyntaxNode node, SemanticModel semanticModel, CommentStyle style)
        {
            if (node is not MethodDeclarationSyntax method)
                return null;

            var methodSymbol = semanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
            if (methodSymbol == null)
                return null;

            var sb = new StringBuilder();
            sb.AppendLine($"Summary: {GenerateSummary(methodSymbol)}");
            foreach (var param in methodSymbol.Parameters)
                sb.AppendLine($"Parameter {param.Name}: {param.Type.ToDisplayString()}");
            if (!methodSymbol.ReturnsVoid)
                sb.AppendLine($"Returns: {methodSymbol.ReturnType.ToDisplayString()}");

            return sb.ToString();
        }

        private static string GenerateSummary(IMethodSymbol method)
        {
            return method.Name switch
            {
                var n when n.StartsWith("Get") => $"Gets the {n.Substring(3)}.",
                var n when n.StartsWith("Set") => $"Sets the {n.Substring(3)}.",
                var n when n.StartsWith("Is") => $"Determines whether {n.Substring(2)} is true.",
                _ => $"Performs the {method.Name} operation."
            };
        }
    }

    public class ComplexConditionCommentProvider : ICommentProvider
    {
        public string GenerateComment(SyntaxNode node, SemanticModel semanticModel, CommentStyle style)
        {
            if (node is IfStatementSyntax ifStmt && HasComplexCondition(ifStmt.Condition))
            {
                string conditionText = ifStmt.Condition.ToString()
                    .Replace("\r\n", " ")
                    .Replace("\n", " ")
                    .Replace("  ", " ");
                return $"Complex condition: {conditionText}";
            }
            return null;
        }

        private static bool HasComplexCondition(ExpressionSyntax condition)
        {
            return condition.DescendantNodes().OfType<BinaryExpressionSyntax>().Count() >= 2;
        }
    }

    public class ClassCommentProvider : ICommentProvider
    {
        public string GenerateComment(SyntaxNode node, SemanticModel semanticModel, CommentStyle style)
        {
            if (node is not ClassDeclarationSyntax classDecl)
                return null;

            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (classSymbol == null)
                return null;

            var sb = new StringBuilder();
            sb.AppendLine($"Class: {classSymbol.Name}");
            if (classSymbol.BaseType != null && classSymbol.BaseType.SpecialType != SpecialType.System_Object)
                sb.AppendLine($"Base class: {classSymbol.BaseType.ToDisplayString()}");
            if (classSymbol.Interfaces.Any())
                sb.AppendLine($"Implements: {string.Join(", ", classSymbol.Interfaces.Select(i => i.ToDisplayString()))}");

            return sb.ToString();
        }
    }
}
