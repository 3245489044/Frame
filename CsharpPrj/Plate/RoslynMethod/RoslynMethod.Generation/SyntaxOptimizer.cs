using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace RoslynSpace.RoslynSyntaxUtilities.Generation
{
    public static class SyntaxOptimizer
    {
        public static string RewriteMethod(string code)
        {
            var tree = CSharpSourceProvider.Instance.Parse(code);
            var root = tree.GetRoot();
            var rewriter = new OptimizedSyntaxRewriter();
            return rewriter.Visit(root).ToFullString();
        }
    }

    public class OptimizedSyntaxRewriter : CSharpSyntaxRewriter
    {
        private static readonly HashSet<SyntaxKind> CommentKindsToRemove = new()
        {
            SyntaxKind.SingleLineCommentTrivia, SyntaxKind.MultiLineCommentTrivia,
            SyntaxKind.SingleLineDocumentationCommentTrivia, SyntaxKind.MultiLineDocumentationCommentTrivia
        };

        private string _currentMethodName = "";
        private string _currentResultListName = "";

        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia) =>
            CommentKindsToRemove.Contains(trivia.Kind()) ? default : base.VisitTrivia(trivia);

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            _currentMethodName = node.Identifier.ValueText;
            _currentResultListName = $"{_currentMethodName}_resultList";

            var result = base.VisitMethodDeclaration(node);

            _currentMethodName = _currentResultListName = "";
            return result;
        }

        public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (IsInsideLambda(node) || string.IsNullOrEmpty(_currentMethodName)) return node;
            if (node.Expression == null) return CreateEmptyStatement(node);

            var returnExpr = node.Expression.ToFullString().Trim();

            if (node.Expression is IdentifierNameSyntax id)
                return CreateStatement($"{_currentResultListName}.Add({id.Identifier.ValueText}.ToString());", node);

            return node;
        }

        private bool IsInsideLambda(SyntaxNode node) =>
            node.Ancestors().Any(a => a is LambdaExpressionSyntax);

        private StatementSyntax CreateEmptyStatement(ReturnStatementSyntax node) =>
            SyntaxFactory.EmptyStatement().WithTriviaFrom(node);

        private StatementSyntax CreateStatement(string code, ReturnStatementSyntax node) =>
            SyntaxFactory.ParseStatement(code).WithTriviaFrom(node);
    }
}
