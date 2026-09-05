using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynSpace.RoslynSyntaxUtilities.Refactoring
{
    public class FieldToParameterRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly List<FieldToParameterTransformer.FieldInfo> _fields;
        private readonly Dictionary<string, FieldToParameterTransformer.FunctionInfo> _functions;
        private readonly Dictionary<string, List<string>> _fieldUsage;
        private readonly List<FieldToParameterTransformer.TransformationLog> _logs;
        private readonly HashSet<string> _processedFields = new HashSet<string>();
        private string _currentFunction;

        public FieldToParameterRewriter(
            SemanticModel semanticModel,
            List<FieldToParameterTransformer.FieldInfo> fields,
            Dictionary<string, FieldToParameterTransformer.FunctionInfo> functions,
            Dictionary<string, List<string>> fieldUsage,
            List<FieldToParameterTransformer.TransformationLog> logs)
        {
            _semanticModel = semanticModel;
            _fields = fields;
            _functions = functions;
            _fieldUsage = fieldUsage;
            _logs = logs;
        }

        #region 函数声明处理

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            _currentFunction = node.Identifier.ValueText;
            var processedNode = ProcessFunctionDeclaration(node, node.ParameterList, node.Body);
            _currentFunction = null;
            return processedNode;
        }

        public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            _currentFunction = node.Identifier.ValueText;
            var processedNode = ProcessFunctionDeclaration(node, node.ParameterList, node.Body);
            _currentFunction = null;
            return processedNode;
        }

        private T ProcessFunctionDeclaration<T>(T node, ParameterListSyntax parameterList, BlockSyntax body) where T : CSharpSyntaxNode
        {
            var usedFields = GetFieldsUsedInFunction(_currentFunction);

            if (!usedFields.Any())
                return node;

            var newParameterList = AddFieldParameters(parameterList, usedFields);
            var newBody = (BlockSyntax)Visit(body);

            return node switch
            {
                MethodDeclarationSyntax method => (T)(object)method.WithParameterList(newParameterList).WithBody(newBody),
                ConstructorDeclarationSyntax constructor => (T)(object)constructor.WithParameterList(newParameterList).WithBody(newBody),
                _ => node
            };
        }

        #endregion

        #region 字段访问处理

        public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (node.Expression is ThisExpressionSyntax &&
                node.Name is IdentifierNameSyntax identifier)
            {
                var fieldName = identifier.Identifier.ValueText;
                if (_fields.Any(f => f.Name == fieldName))
                {
                    _processedFields.Add(fieldName);

                    if (!string.IsNullOrEmpty(_currentFunction))
                    {
                        _logs.Add(new FieldToParameterTransformer.TransformationLog
                        {
                            Type = "TRANSFORM",
                            Message = $"将字段 '{fieldName}' 转换为参数",
                            FunctionName = _currentFunction
                        });
                    }

                    return SyntaxFactory.IdentifierName(fieldName);
                }
            }

            return base.VisitMemberAccessExpression(node);
        }

        public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        {
            var fieldName = node.Identifier.ValueText;

            if (_fields.Any(f => f.Name == fieldName) &&
                !string.IsNullOrEmpty(_currentFunction) &&
                IsFieldAccessInCurrentClass(node))
            {
                _processedFields.Add(fieldName);

                _logs.Add(new FieldToParameterTransformer.TransformationLog
                {
                    Type = "TRANSFORM",
                    Message = $"将字段 '{fieldName}' 转换为参数",
                    FunctionName = _currentFunction
                });

                return node;
            }

            return base.VisitIdentifierName(node);
        }

        private bool IsFieldAccessInCurrentClass(IdentifierNameSyntax node)
        {
            if (string.IsNullOrEmpty(_currentFunction))
                return false;

            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            if (symbolInfo.Symbol is IFieldSymbol fieldSymbol)
            {
                var containingType = fieldSymbol.ContainingType;
                if (containingType != null)
                {
                    var methodSymbol = _semanticModel.LookupSymbols(node.SpanStart)
                        .OfType<IMethodSymbol>()
                        .FirstOrDefault(m => m.Name == _currentFunction);
                    if (methodSymbol != null)
                    {
                        return SymbolEqualityComparer.Default.Equals(containingType, methodSymbol.ContainingType);
                    }
                }
            }

            return false;
        }

        #endregion

        #region 函数调用处理

        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var newArguments = ProcessFunctionCallArguments(node.ArgumentList, node.Expression);
            return node.WithArgumentList(newArguments);
        }

        public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var newArguments = ProcessFunctionCallArguments(node.ArgumentList, node.Type);
            return node.WithArgumentList(newArguments);
        }

        private ArgumentListSyntax ProcessFunctionCallArguments(ArgumentListSyntax argumentList, SyntaxNode target)
        {
            if (argumentList == null) return argumentList;

            var calledFunctionName = GetCalledFunctionName(target);
            if (string.IsNullOrEmpty(calledFunctionName))
                return argumentList;

            var usedFields = GetFieldsUsedInFunction(calledFunctionName);
            if (!usedFields.Any())
                return argumentList;

            var newArguments = argumentList.Arguments.ToList();
            foreach (var field in usedFields)
            {
                var fieldInfo = _fields.FirstOrDefault(f => f.Name == field);
                if (fieldInfo != null)
                {
                    var fieldAccess = SyntaxFactory.IdentifierName(field);
                    var argument = SyntaxFactory.Argument(fieldAccess);
                    newArguments.Add(argument);

                    if (!string.IsNullOrEmpty(_currentFunction))
                    {
                        _logs.Add(new FieldToParameterTransformer.TransformationLog
                        {
                            Type = "CALL_CHAIN",
                            Message = $"在调用 '{calledFunctionName}' 时添加字段参数 '{field}'",
                            FunctionName = _currentFunction
                        });
                    }
                }
            }

            return argumentList.WithArguments(SyntaxFactory.SeparatedList(newArguments));
        }

        private string GetCalledFunctionName(SyntaxNode target)
        {
            return target switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax memberAccess => (memberAccess.Name as IdentifierNameSyntax)?.Identifier.ValueText,
                TypeSyntax type => type.ToString(),
                _ => null
            };
        }

        #endregion

        #region 辅助方法

        private List<string> GetFieldsUsedInFunction(string functionName)
        {
            return _fieldUsage
                .Where(kvp => kvp.Value.Contains(functionName))
                .Select(kvp => kvp.Key)
                .ToList();
        }

        private ParameterListSyntax AddFieldParameters(ParameterListSyntax parameterList, List<string> fields)
        {
            var parameters = parameterList.Parameters.ToList();

            foreach (var fieldName in fields)
            {
                var fieldInfo = _fields.FirstOrDefault(f => f.Name == fieldName);
                if (fieldInfo != null && !parameters.Any(p => p.Identifier.ValueText == fieldName))
                {
                    var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(fieldName))
                        .WithType(SyntaxFactory.ParseTypeName(fieldInfo.Type));

                    parameters.Add(parameter);
                }
            }

            return parameterList.WithParameters(SyntaxFactory.SeparatedList(parameters));
        }

        #endregion

        #region 字段声明清理

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var processedNode = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);

            var fieldsToRemove = _fields.Where(f => _processedFields.Contains(f.Name)).ToList();
            if (!fieldsToRemove.Any())
                return processedNode;

            var newMembers = new List<MemberDeclarationSyntax>();
            foreach (var member in processedNode.Members)
            {
                if (member is FieldDeclarationSyntax fieldDeclaration)
                {
                    var remainingVariables = fieldDeclaration.Declaration.Variables
                        .Where(v => !fieldsToRemove.Any(f => f.Name == v.Identifier.ValueText))
                        .ToList();

                    if (remainingVariables.Any())
                    {
                        var newDeclaration = fieldDeclaration.Declaration.WithVariables(
                            SyntaxFactory.SeparatedList(remainingVariables));
                        newMembers.Add(fieldDeclaration.WithDeclaration(newDeclaration));
                    }
                }
                else
                {
                    newMembers.Add(member);
                }
            }

            return processedNode.WithMembers(SyntaxFactory.List(newMembers));
        }

        #endregion
    }
}
