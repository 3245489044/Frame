using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynSpace.RoslynSyntaxUtilities.Refactoring
{
    public class MethodTransformer
    {
        public static string TransformMethods(string code, string methodName)
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();
            var rewriter = new MethodRewriter(methodName);
            var newRoot = rewriter.Visit(root);
            return newRoot.NormalizeWhitespace().ToFullString();
        }

        private class MethodRewriter : CSharpSyntaxRewriter
        {
            private readonly string _targetMethodName;

            public MethodRewriter(string targetMethodName)
            {
                _targetMethodName = targetMethodName;
            }

            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                if (node.Identifier.ValueText != _targetMethodName)
                    return base.VisitMethodDeclaration(node);

                return CreateDelegateField(node);
            }

            private FieldDeclarationSyntax CreateDelegateField(MethodDeclarationSyntax originalMethod)
            {
                var isVoidMethod = IsVoidReturnType(originalMethod.ReturnType);

                var assignment = CreateDelegateAssignment(originalMethod, isVoidMethod);

                return SyntaxFactory.FieldDeclaration(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.GenericName(SyntaxFactory.Identifier("Func"))
                            .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(
                                SyntaxFactory.SeparatedList<TypeSyntax>(new[]
                                {
                                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)),
                                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword))
                                })))
                    )
                    .WithVariables(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(originalMethod.Identifier.ValueText))
                                .WithInitializer(SyntaxFactory.EqualsValueClause(assignment))
                        )
                    )
                )
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithLeadingTrivia(originalMethod.GetLeadingTrivia())
                .WithTrailingTrivia(originalMethod.GetTrailingTrivia());
            }

            private ExpressionSyntax CreateDelegateAssignment(MethodDeclarationSyntax originalMethod, bool isVoidMethod)
            {
                var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("arg"))
                    .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)));

                var body = CreateLambdaBody(originalMethod, isVoidMethod);

                return SyntaxFactory.ParenthesizedLambdaExpression(
                    SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(parameter)),
                    body
                );
            }

            private CSharpSyntaxNode CreateLambdaBody(MethodDeclarationSyntax originalMethod, bool isVoidMethod)
            {
                var originalBody = originalMethod.Body ?? SyntaxFactory.Block();
                var statements = new List<StatementSyntax>();

                if (originalMethod.ParameterList.Parameters.Count > 0)
                {
                    statements.AddRange(CreateParameterExtractionStatements(originalMethod.ParameterList));
                }

                var transformedStatements = TransformStatements(originalBody.Statements, isVoidMethod);
                statements.AddRange(transformedStatements);

                if (!HasReturnStatement(originalBody))
                {
                    statements.Add(CreateDefaultReturnStatement());
                }

                return SyntaxFactory.Block(statements);
            }

            private List<StatementSyntax> CreateParameterExtractionStatements(ParameterListSyntax originalParameters)
            {
                var statements = new List<StatementSyntax>();

                var listType = SyntaxFactory.GenericName(SyntaxFactory.Identifier("List"))
                    .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(
                        SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)))));

                var asExpression = SyntaxFactory.BinaryExpression(
                    SyntaxKind.AsExpression,
                    SyntaxFactory.IdentifierName("arg"),
                    listType);

                var coalesceExpression = SyntaxFactory.BinaryExpression(
                    SyntaxKind.CoalesceExpression,
                    asExpression,
                    SyntaxFactory.ObjectCreationExpression(listType)
                        .WithArgumentList(SyntaxFactory.ArgumentList()));

                statements.Add(SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("argsList"))
                        .WithInitializer(SyntaxFactory.EqualsValueClause(coalesceExpression))))));

                statements.AddRange(originalParameters.Parameters.Select((param, index) =>
                    CreateParameterExtraction(param, index)));

                return statements;
            }

            private StatementSyntax CreateParameterExtraction(ParameterSyntax parameter, int index)
            {
                var listAccess = SyntaxFactory.ElementAccessExpression(SyntaxFactory.IdentifierName("argsList"))
                    .WithArgumentList(SyntaxFactory.BracketedArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.NumericLiteralExpression,
                                    SyntaxFactory.Literal(index))))));

                return SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(parameter.Identifier.ValueText))
                        .WithInitializer(SyntaxFactory.EqualsValueClause(
                            SyntaxFactory.CastExpression(parameter.Type, listAccess))))));
            }

            private List<StatementSyntax> TransformStatements(SyntaxList<StatementSyntax> originalStatements, bool isVoidMethod)
            {
                return originalStatements.Select(statement => TransformStatement(statement, isVoidMethod)).ToList();
            }

            private StatementSyntax TransformStatement(StatementSyntax statement, bool isVoidMethod)
            {
                return statement switch
                {
                    ReturnStatementSyntax returnStatement => TransformReturnStatement(returnStatement, isVoidMethod),
                    BlockSyntax block => block.WithStatements(SyntaxFactory.List(TransformStatements(block.Statements, isVoidMethod))),
                    IfStatementSyntax ifStatement => TransformIfStatement(ifStatement, isVoidMethod),
                    ForStatementSyntax forStatement => forStatement.WithStatement(TransformStatement(forStatement.Statement, isVoidMethod)),
                    WhileStatementSyntax whileStatement => whileStatement.WithStatement(TransformStatement(whileStatement.Statement, isVoidMethod)),
                    ForEachStatementSyntax forEachStatement => forEachStatement.WithStatement(TransformStatement(forEachStatement.Statement, isVoidMethod)),
                    _ => statement
                };
            }

            private StatementSyntax TransformReturnStatement(ReturnStatementSyntax returnStatement, bool isVoidMethod)
            {
                if (isVoidMethod)
                {
                    return SyntaxFactory.ReturnStatement(
                        SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));
                }
                else
                {
                    return returnStatement.Expression != null
                        ? SyntaxFactory.ReturnStatement(
                            SyntaxFactory.CastExpression(
                                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)),
                                returnStatement.Expression))
                        : SyntaxFactory.ReturnStatement(
                            SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));
                }
            }

            private IfStatementSyntax TransformIfStatement(IfStatementSyntax ifStatement, bool isVoidMethod)
            {
                return ifStatement
                    .WithStatement(TransformStatement(ifStatement.Statement, isVoidMethod))
                    .WithElse(ifStatement.Else?.WithStatement(TransformStatement(ifStatement.Else.Statement, isVoidMethod)));
            }

            private bool HasReturnStatement(BlockSyntax body)
            {
                return body.DescendantNodes().OfType<ReturnStatementSyntax>().Any();
            }

            private bool IsVoidReturnType(TypeSyntax returnType)
            {
                return returnType is PredefinedTypeSyntax predefinedType &&
                       predefinedType.Keyword.IsKind(SyntaxKind.VoidKeyword);
            }

            private StatementSyntax CreateDefaultReturnStatement()
            {
                return SyntaxFactory.ReturnStatement(
                    SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));
            }
        }
    }
}
