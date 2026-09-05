using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    public class CodeTransformSkeleton
    {
        public MethodDeclarationSyntax CreateMethod(string returnType, string name, params ParameterSyntax[] parameters)
        {
            return SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.IdentifierName(returnType),
                    SyntaxFactory.Identifier(name))
                .WithParameterList(SyntaxFactory.ParameterList(
                    SyntaxFactory.SeparatedList(parameters)))
                .WithBody(SyntaxFactory.Block());
        }

        public PropertyDeclarationSyntax CreateAutoProperty(string type, string name, AccessibilityLevel accessibility)
        {
            var property = SyntaxFactory.PropertyDeclaration(
                    SyntaxFactory.IdentifierName(type),
                    SyntaxFactory.Identifier(name))
                .WithAccessorList(SyntaxFactory.AccessorList(
                    SyntaxFactory.List(new[]
                    {
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                    })));

            return accessibility switch
            {
                AccessibilityLevel.Public => property.AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword)),
                AccessibilityLevel.Private => property.AddModifiers(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)),
                AccessibilityLevel.Internal => property.AddModifiers(SyntaxFactory.Token(SyntaxKind.InternalKeyword)),
                AccessibilityLevel.Protected => property.AddModifiers(SyntaxFactory.Token(SyntaxKind.ProtectedKeyword)),
                _ => property
            };
        }

        public ExpressionStatementSyntax ConvertToStatement(InvocationExpressionSyntax invocation)
            => StatementTransformer.ToExpressionStatement(invocation);

        public T RenameIdentifier<T>(T node, string oldName, string newName) where T : SyntaxNode
            => StatementTransformer.ReplaceIdentifier(node, oldName, newName);

        public T AddStatementsToBlock<T>(T node, params StatementSyntax[] statements) where T : SyntaxNode
            => StatementTransformer.AddStatements(node, statements);
    }
}
