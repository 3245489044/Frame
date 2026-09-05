using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    internal static class StatementFactory
    {
        public static IfStatementSyntax CreateIfStatement(ExpressionSyntax condition, StatementSyntax thenStatement, StatementSyntax elseStatement = null)
        {
            var ifStmt = SyntaxFactory.IfStatement(condition, SyntaxFactory.Block(thenStatement));
            if (elseStatement != null)
                ifStmt = ifStmt.WithElse(SyntaxFactory.ElseClause(SyntaxFactory.Block(elseStatement)));
            return ifStmt;
        }

        public static ForStatementSyntax CreateForStatement(string init, string condition, string increment, params StatementSyntax[] body)
        {
            var forStmt = SyntaxFactory.ForStatement(SyntaxFactory.Block(body))
                .WithDeclaration(SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("i"))
                            .WithInitializer(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0)))))))
                .WithCondition(SyntaxFactory.ParseExpression(condition))
                .WithIncrementors(SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(SyntaxFactory.ParseExpression(increment)));
            return forStmt;
        }

        public static ForEachStatementSyntax CreateForEachStatement(string type, string identifier, ExpressionSyntax expression, params StatementSyntax[] body)
            => SyntaxFactory.ForEachStatement(
                SyntaxFactory.IdentifierName(type),
                SyntaxFactory.Identifier(identifier),
                expression,
                SyntaxFactory.Block(body));

        public static WhileStatementSyntax CreateWhileStatement(ExpressionSyntax condition, params StatementSyntax[] body)
            => SyntaxFactory.WhileStatement(condition, SyntaxFactory.Block(body));

        public static ReturnStatementSyntax CreateReturnStatement(ExpressionSyntax expression = null)
            => expression == null
                ? SyntaxFactory.ReturnStatement()
                : SyntaxFactory.ReturnStatement(expression);

        public static LocalDeclarationStatementSyntax CreateLocalVariable(string type, string name, ExpressionSyntax initializer)
            => SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.IdentifierName(type),
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name))
                            .WithInitializer(SyntaxFactory.EqualsValueClause(initializer)))));

        public static ExpressionStatementSyntax CreateAssignment(string left, ExpressionSyntax right)
            => SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(left),
                    right));

        public static ExpressionStatementSyntax CreateInvocationStatement(string methodName, params ExpressionSyntax[] arguments)
            => SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.IdentifierName(methodName),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments.Select(a => SyntaxFactory.Argument(a))))));
    }

    internal static class StatementTransformer
    {
        public static ExpressionStatementSyntax ToExpressionStatement(InvocationExpressionSyntax invocation)
            => SyntaxFactory.ExpressionStatement(invocation);

        public static InvocationExpressionSyntax ToInvocation(ExpressionStatementSyntax expressionStatement)
            => expressionStatement.Expression is InvocationExpressionSyntax invocation ? invocation : null;

        public static BlockSyntax ToBlock(StatementSyntax statement)
            => SyntaxFactory.Block(statement);

        public static T ReplaceIdentifier<T>(T node, string oldName, string newName) where T : SyntaxNode
            => node.ReplaceTokens(
                node.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken) && t.Text == oldName),
                (oldToken, _) => SyntaxFactory.Identifier(newName).WithTriviaFrom(oldToken));

        public static T AddStatements<T>(T node, params StatementSyntax[] statements) where T : SyntaxNode
        {
            if (node is BlockSyntax block)
                return (T)(SyntaxNode)block.AddStatements(statements);
            return node;
        }
    }

    public static class StatementSimilarityCalculator
    {
        public static double Calculate(StatementSyntax a, StatementSyntax b)
        {
            if (a == null || b == null) return 0.0;
            string textA = a.NormalizeWhitespace().ToString();
            string textB = b.NormalizeWhitespace().ToString();

            if (textA == textB) return 1.0;
            double structuralSim = CalculateStructuralSimilarity(a, b);
            if (structuralSim > 0.9) return structuralSim;

            int distance = ComputeLevenshteinDistance(textA, textB);
            int maxLength = Math.Max(textA.Length, textB.Length);
            return maxLength == 0 ? 1.0 : 1.0 - (double)distance / maxLength;
        }

        public static double CalculateStructuralSimilarity(StatementSyntax a, StatementSyntax b)
        {
            if (a == null || b == null) return 0.0;
            if (a.GetType() != b.GetType()) return 0.0;

            if (a is ExpressionStatementSyntax exprA && b is ExpressionStatementSyntax exprB)
                return CalculateExpressionSimilarity(exprA.Expression, exprB.Expression);

            return 0.5;
        }

        public static double CalculateExpressionSimilarity(ExpressionSyntax a, ExpressionSyntax b)
        {
            if (a == null || b == null) return 0.0;
            if (a.GetType() != b.GetType()) return 0.3;

            if (a is InvocationExpressionSyntax invokeA && b is InvocationExpressionSyntax invokeB)
            {
                var memberA = invokeA.Expression.ToString();
                var memberB = invokeB.Expression.ToString();
                return memberA == memberB ? 0.9 : 0.4;
            }

            return 0.6;
        }

        public static string NormalizeText(StatementSyntax stmt)
        {
            if (stmt == null) return string.Empty;
            var normalized = stmt.NormalizeWhitespace().ToString();
            var sb = new StringBuilder(normalized.Length);
            foreach (char c in normalized)
                if (c != '\r' && c != '\n' && c != ' ' && c != '\t')
                    sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        public static bool AreEqual(StatementSyntax a, StatementSyntax b) =>
            a == null || b == null ? a == b : NormalizeText(a) == NormalizeText(b);

        public static int ComputeLevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
            if (string.IsNullOrEmpty(b)) return a.Length;
            int lenA = a.Length, lenB = b.Length;
            var prevRow = new int[lenB + 1];
            var currRow = new int[lenB + 1];
            for (int j = 0; j <= lenB; j++) prevRow[j] = j;

            for (int i = 1; i <= lenA; i++)
            {
                currRow[0] = i;
                for (int j = 1; j <= lenB; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    currRow[j] = Math.Min(Math.Min(prevRow[j] + 1, currRow[j - 1] + 1), prevRow[j - 1] + cost);
                }
                (prevRow, currRow) = (currRow, prevRow);
            }
            return prevRow[lenB];
        }
    }
}
