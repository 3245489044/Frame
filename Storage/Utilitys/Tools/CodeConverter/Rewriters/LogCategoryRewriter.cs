using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynStringRefactoringFramework;

namespace CodeConverter.Rewriters
{
    public class LogCategoryReplacementRule : RefactoringRule
    {
        private static readonly string[] ModulePrefixes =
        [
            "WdUI_",
            "FileDriven_",
            "Launcher_",
            "FunctionStorage_",
            "Batch_"
        ];

        public LogCategoryReplacementRule() : base("LogCategoryToLogCategories") { Priority = 10; }

        public override SyntaxNode Transform(SyntaxNode root, SemanticModel semanticModel)
        {
            var rewriter = new LogCategoryRewriter();
            return rewriter.Visit(root);
        }

        public int GetReplacementCount(SyntaxNode root)
        {
            var rewriter = new LogCategoryRewriter();
            rewriter.Visit(root);
            return rewriter.ReplacementCount;
        }

        private class LogCategoryRewriter : CSharpSyntaxRewriter
        {
            public int ReplacementCount { get; private set; }

            public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                var visitedNode = (MemberAccessExpressionSyntax?)base.VisitMemberAccessExpression(node);
                if (visitedNode == null) return null;

                if (visitedNode.Expression is IdentifierNameSyntax identifier &&
                    identifier.Identifier.Text == "LogCategory" &&
                    visitedNode.Name is IdentifierNameSyntax memberName)
                {
                    var memberNameText = memberName.Identifier.Text;

                    foreach (var prefix in ModulePrefixes)
                    {
                        if (memberNameText.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            ReplacementCount++;

                            return SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.IdentifierName("LogCategories"),
                                SyntaxFactory.IdentifierName(memberNameText))
                                .WithTriviaFrom(visitedNode);
                        }
                    }
                }

                return visitedNode;
            }
        }
    }
}
