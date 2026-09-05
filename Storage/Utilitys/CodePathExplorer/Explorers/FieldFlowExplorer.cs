using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodePathExplorer.Models;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using RoslynSpace.RoslynSyntaxUtilities.Core.Utilities;

namespace CodePathExplorer.Explorers;

public class FieldFlowExplorer
{
    private readonly AnalysisContext _context;
    private readonly Dictionary<string, List<FieldDeclarationSyntax>> _classFields = new();

    public FieldFlowExplorer(AnalysisContext context)
    {
        _context = context;
        CollectAllFields();
    }

    private void CollectAllFields()
    {
        var classes = _context.Root.DescendantNodes().OfType<ClassDeclarationSyntax>();
        foreach (var cls in classes)
        {
            var className = cls.Identifier.Text;
            var fields = cls.Members.OfType<FieldDeclarationSyntax>().ToList();
            if (fields.Count > 0)
                _classFields[className] = fields;
        }
    }

    public List<FieldFlowSet> ExploreAll()
    {
        var result = new List<FieldFlowSet>();

        foreach (var (className, fields) in _classFields)
        {
            foreach (var field in fields)
            {
                var flowSet = AnalyzeFieldFlow(field, className);
                if (flowSet != null)
                    result.Add(flowSet);
            }
        }

        return result;
    }

    private FieldFlowSet? AnalyzeFieldFlow(FieldDeclarationSyntax fieldDecl, string className)
    {
        var classDecl = fieldDecl.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl == null) return null;

        var flowSet = new FieldFlowSet
        {
            ClassName = className,
            FieldType = fieldDecl.Declaration.Type.ToString()
        };

        foreach (var variable in fieldDecl.Declaration.Variables)
        {
            var fieldName = variable.Identifier.Text;
            flowSet.FieldName = fieldName;

            int flowId = 0;

            var declNode = new FieldFlowNode
            {
                OperationType = FieldOperationType.Declaration,
                MethodFullName = $"{className}.(字段声明)",
                ClassName = className,
                MethodName = "(字段声明)",
                LineNumber = SyntaxHelper.GetLineNumber(variable)
            };

            var fieldRefs = new List<(string memberName, SyntaxNode node, bool isWrite)>();

            var members = classDecl.Members
                .Where(m => m is MethodDeclarationSyntax or PropertyDeclarationSyntax or ConstructorDeclarationSyntax)
                .ToList();

            foreach (var member in members)
            {
                FindFieldReferences(member, fieldName, className, fieldRefs);
            }

            if (fieldRefs.Count == 0)
            {
                var flow = new FieldFlow
                {
                    FlowId = ++flowId,
                    FieldName = fieldName,
                    FieldType = flowSet.FieldType,
                    ClassName = className
                };
                flow.Nodes.Add(new FieldFlowNode
                {
                    OperationType = FieldOperationType.Declaration,
                    MethodFullName = declNode.MethodFullName,
                    ClassName = declNode.ClassName,
                    MethodName = declNode.MethodName,
                    LineNumber = declNode.LineNumber
                });
                flowSet.Flows.Add(flow);
            }
            else
            {
                foreach (var (memberName, node, isWrite) in fieldRefs)
                {
                    var flow = new FieldFlow
                    {
                        FlowId = ++flowId,
                        FieldName = fieldName,
                        FieldType = flowSet.FieldType,
                        ClassName = className
                    };

                    flow.Nodes.Add(new FieldFlowNode
                    {
                        OperationType = FieldOperationType.Declaration,
                        MethodFullName = declNode.MethodFullName,
                        ClassName = declNode.ClassName,
                        MethodName = declNode.MethodName,
                        LineNumber = declNode.LineNumber
                    });

                    flow.Nodes.Add(new FieldFlowNode
                    {
                        OperationType = isWrite ? FieldOperationType.Write : FieldOperationType.Read,
                        MethodFullName = $"{className}.{memberName}",
                        ClassName = className,
                        MethodName = memberName,
                        LineNumber = SyntaxHelper.GetLineNumber(node)
                    });

                    if (node.Parent is ArgumentSyntax arg && arg.Parent is ArgumentListSyntax argList)
                    {
                        var invocation = argList.Parent as InvocationExpressionSyntax;
                        if (invocation != null)
                        {
                            var targetMethod = ResolveCalledMethod(invocation);
                            if (targetMethod != null)
                            {
                                flow.Nodes.Add(new FieldFlowNode
                                {
                                    OperationType = FieldOperationType.PassAsArgument,
                                    MethodFullName = $"{className}.{memberName}",
                                    ClassName = className,
                                    MethodName = memberName,
                                    LineNumber = SyntaxHelper.GetLineNumber(invocation),
                                    PassedToMethod = targetMethod
                                });
                            }
                        }
                    }

                    flowSet.Flows.Add(flow);
                }
            }
        }

        return flowSet;
    }

    private void FindFieldReferences(
        SyntaxNode node,
        string fieldName,
        string className,
        List<(string memberName, SyntaxNode node, bool isWrite)> refs)
    {
        var memberName = GetNodeMemberName(node);

        var identifiers = node.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(id => id.Identifier.Text == fieldName)
            .ToList();

        foreach (var id in identifiers)
        {
            if (!IsFieldReference(id, fieldName, className))
                continue;

            bool isWrite = IsWriteContext(id);
            refs.Add((memberName, id, isWrite));
        }
    }

    private bool IsFieldReference(IdentifierNameSyntax identifier, string fieldName, string className)
    {
        if (_context.SemanticModel != null)
        {
            var symbolInfo = _context.SemanticModel.GetSymbolInfo(identifier);
            var symbol = symbolInfo.Symbol;
            if (symbol is IFieldSymbol)
                return true;
            if (symbol is ILocalSymbol or IParameterSymbol)
                return false;
        }

        var containingMember = identifier.Ancestors()
            .FirstOrDefault(a => a is MethodDeclarationSyntax or ConstructorDeclarationSyntax or PropertyDeclarationSyntax);

        if (containingMember == null) return false;

        if (identifier.Parent is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name == identifier &&
            memberAccess.Expression is ThisExpressionSyntax)
        {
            return true;
        }

        var localVariables = containingMember.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(v => v.Identifier.Text == fieldName)
            .ToList();

        var parameters = containingMember.DescendantNodes()
            .OfType<ParameterSyntax>()
            .Where(p => p.Identifier.Text == fieldName)
            .ToList();

        var identifierLine = identifier.GetLocation().GetLineSpan().StartLinePosition.Line;

        foreach (var localVar in localVariables)
        {
            var varLine = localVar.GetLocation().GetLineSpan().StartLinePosition.Line;
            if (varLine < identifierLine)
                return false;
        }

        foreach (var param in parameters)
        {
            var paramLine = param.GetLocation().GetLineSpan().StartLinePosition.Line;
            if (paramLine < identifierLine)
                return false;
        }

        return true;
    }

    private static bool IsWriteContext(IdentifierNameSyntax identifier)
    {
        if (identifier.Parent is AssignmentExpressionSyntax assignment)
        {
            return assignment.Left == identifier;
        }

        if (identifier.Parent is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name == identifier)
        {
            if (memberAccess.Parent is AssignmentExpressionSyntax assignment2)
                return assignment2.Left == memberAccess;
        }

        if (identifier.Parent is ArgumentSyntax arg)
        {
            if (arg.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword) ||
                arg.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword))
                return true;
        }

        if (identifier.Parent is PostfixUnaryExpressionSyntax postfix &&
            (postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
             postfix.IsKind(SyntaxKind.PostDecrementExpression)))
            return true;

        if (identifier.Parent is PrefixUnaryExpressionSyntax prefix &&
            (prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
             prefix.IsKind(SyntaxKind.PreDecrementExpression)))
            return true;

        return false;
    }

    private static string? ResolveCalledMethod(InvocationExpressionSyntax invocation)
    {
        var expr = invocation.Expression;
        if (expr is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Name.Identifier.Text;
        if (expr is IdentifierNameSyntax identifier)
            return identifier.Identifier.Text;
        return null;
    }

    private static string GetNodeMemberName(SyntaxNode node)
    {
        if (node is MethodDeclarationSyntax methodSelf)
            return methodSelf.Identifier.Text;
        if (node is PropertyDeclarationSyntax propSelf)
            return propSelf.Identifier.Text;
        if (node is ConstructorDeclarationSyntax ctorSelf)
            return ctorSelf.Identifier.Text;

        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method != null) return method.Identifier.Text;

        var prop = node.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if (prop != null) return prop.Identifier.Text;

        var ctor = node.Ancestors().OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (ctor != null) return ctor.Identifier.Text;

        return "(未知)";
    }
}
