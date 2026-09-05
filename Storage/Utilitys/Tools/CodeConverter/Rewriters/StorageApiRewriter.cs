
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynStringRefactoringFramework;

namespace CodeConverter.Rewriters
{
    public class StorageApiReplacementRule : RefactoringRule
    {
        public StorageApiReplacementRule() : base("StorageApiReplacement") { Priority = 10; }

        public override SyntaxNode Transform(SyntaxNode root, SemanticModel semanticModel)
        {
            var rewriter = new StorageApiRewriter();
            return rewriter.Visit(root);
        }

        public int GetReplacementCount(SyntaxNode root)
        {
            var rewriter = new StorageApiRewriter();
            rewriter.Visit(root);
            return rewriter.ReplacementCount;
        }

        private class StorageApiRewriter : CSharpSyntaxRewriter
        {
            public int ReplacementCount { get; private set; }

            public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                var visitedNode = (ObjectCreationExpressionSyntax?)base.VisitObjectCreationExpression(node);
                if (visitedNode == null) return null;

                if (visitedNode.Type is IdentifierNameSyntax typeName &&
                    typeName.Identifier.Text == "SqliteIsolatedStorage" &&
                    visitedNode.ArgumentList != null &&
                    visitedNode.ArgumentList.Arguments.Count == 2)
                {
                    var newArgs = SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(visitedNode.ArgumentList.Arguments[1])
                    ).WithTriviaFrom(visitedNode.ArgumentList);

                    ReplacementCount++;
                    return visitedNode.WithArgumentList(newArgs);
                }

                return visitedNode;
            }

            public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var visitedNode = (InvocationExpressionSyntax?)base.VisitInvocationExpression(node);
                if (visitedNode == null) return null;

                if (visitedNode.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    var methodName = memberAccess.Name.Identifier.Text;

                    if (methodName == "Store")
                    {
                        ReplacementCount++;
                        var newMemberAccess = memberAccess.WithName(
                            SyntaxFactory.IdentifierName("WriteBatch").WithTriviaFrom(memberAccess.Name));
                        return visitedNode.WithExpression(newMemberAccess);
                    }

                    if (methodName == "GetAll")
                    {
                        ReplacementCount++;
                        var filterCreation = SyntaxFactory.ObjectCreationExpression(
                            SyntaxFactory.IdentifierName("LogQueryFilter"))
                            .WithInitializer(SyntaxFactory.InitializerExpression(
                                SyntaxKind.ObjectInitializerExpression,
                                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                                    SyntaxFactory.AssignmentExpression(
                                        SyntaxKind.SimpleAssignmentExpression,
                                        SyntaxFactory.IdentifierName("Limit"),
                                        SyntaxFactory.MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            SyntaxFactory.IdentifierName("int"),
                                            SyntaxFactory.IdentifierName("MaxValue")
                                        )
                                    )
                                )
                            ));

                        var newMemberAccess = memberAccess.WithName(
                            SyntaxFactory.IdentifierName("Query").WithTriviaFrom(memberAccess.Name));
                        var newArgs = SyntaxFactory.ArgumentList(
                            SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(filterCreation))
                        );

                        return visitedNode
                            .WithExpression(newMemberAccess)
                            .WithArgumentList(newArgs);
                    }
                }

                return visitedNode;
            }

            public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                var visitedNode = (MemberAccessExpressionSyntax?)base.VisitMemberAccessExpression(node);
                if (visitedNode == null) return null;

                if (visitedNode.Name is IdentifierNameSyntax memberName &&
                    memberName.Identifier.Text == "TableName")
                {
                    ReplacementCount++;
                    return SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal("logs")
                    ).WithTriviaFrom(visitedNode);
                }

                return visitedNode;
            }
        }
    }
}
