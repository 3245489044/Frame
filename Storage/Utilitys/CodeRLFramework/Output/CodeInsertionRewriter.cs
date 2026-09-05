using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeRLFramework.Output
{
    public class CodeInsertionRewriter : CSharpSyntaxRewriter
    {
        private readonly List<FunctionCall> _calls;
        private int _callIndex;

        public CodeInsertionRewriter(List<FunctionCall> calls)
        {
            _calls = calls ?? throw new ArgumentNullException(nameof(calls));
            _callIndex = 0;
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (node.Body != null)
            {
                var existingStatements = node.Body.Statements.ToList();
                var newStatements = new List<StatementSyntax>();

                foreach (var stmt in existingStatements)
                {
                    if (IsPlaceholder(stmt))
                    {
                        while (_callIndex < _calls.Count)
                        {
                            var call = _calls[_callIndex++];
                            newStatements.Add(CreateCallStatement(call));
                        }
                    }
                    else
                    {
                        newStatements.Add(stmt);
                    }
                }

                while (_callIndex < _calls.Count)
                {
                    var call = _calls[_callIndex++];
                    newStatements.Add(CreateCallStatement(call));
                }

                var newBody = node.Body.WithStatements(SyntaxFactory.List(newStatements));
                return node.WithBody(newBody);
            }

            if (node.ExpressionBody != null)
            {
                var statements = new List<StatementSyntax>();

                while (_callIndex < _calls.Count)
                {
                    var call = _calls[_callIndex++];
                    statements.Add(CreateCallStatement(call));
                }

                var returnStatement = SyntaxFactory.ReturnStatement(node.ExpressionBody.Expression);
                statements.Add(returnStatement);

                var newBody = SyntaxFactory.Block(statements);
                return node.WithBody(newBody).WithExpressionBody(null);
            }

            return base.VisitMethodDeclaration(node);
        }

        private static bool IsPlaceholder(StatementSyntax stmt)
        {
            var text = stmt.ToString();

            if (text.Contains("TODO", StringComparison.OrdinalIgnoreCase))
                return true;

            if (text.Contains("// 填充代码", StringComparison.OrdinalIgnoreCase))
                return true;

            if (text.Contains("// 填充", StringComparison.OrdinalIgnoreCase))
                return true;

            if (stmt is ExpressionStatementSyntax exprStmt)
            {
                if (exprStmt.Expression is LiteralExpressionSyntax literal &&
                    literal.IsKind(SyntaxKind.DefaultLiteralExpression))
                    return true;

                if (exprStmt.Expression is IdentifierNameSyntax id &&
                    string.IsNullOrEmpty(id.Identifier.Text))
                    return true;
            }

            return false;
        }

        private static StatementSyntax CreateCallStatement(FunctionCall call)
        {
            var arguments = call.Parameters
                .Select(p => SyntaxFactory.Argument(
                    SyntaxFactory.IdentifierName(p.Value ?? p.Name)))
                .ToList();

            var invocation = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.IdentifierName(call.FunctionName))
                .WithArgumentList(SyntaxFactory.ArgumentList(
                    SyntaxFactory.SeparatedList(arguments)));

            return SyntaxFactory.ExpressionStatement(invocation);
        }
    }
}
