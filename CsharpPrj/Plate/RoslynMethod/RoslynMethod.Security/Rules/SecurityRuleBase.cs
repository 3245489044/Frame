using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace RoslynSpace.RoslynSyntaxUtilities.Security
{
    public abstract class SecurityRuleBase : ISecurityRule
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract string Description { get; }

        public abstract void Analyze(SyntaxNode node, SemanticModel model, List<SecurityIssue> issues);

        protected void Report(SyntaxNode node, List<SecurityIssue> issues)
        {
            int? line = null;
            int? column = null;
            if (node.GetLocation().GetLineSpan().IsValid)
            {
                var span = node.GetLocation().GetLineSpan();
                line = span.StartLinePosition.Line + 1;
                column = span.StartLinePosition.Character + 1;
            }

            issues.Add(new SecurityIssue
            {
                RuleId = Id,
                Title = Title,
                Description = Description,
                Severity = ErrorSeverity.SecurityError,
                Line = line,
                Column = column,
                CodeSnippet = node.ToString()
            });
        }

        protected bool IsDerivedFrom(ITypeSymbol type, ITypeSymbol baseType)
        {
            var current = type;
            while (current != null)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                    return true;
                current = current.BaseType;
            }
            return false;
        }

        protected string GetMethodFullName(IMethodSymbol method)
        {
            if (method.ContainingType == null)
                return method.Name;
            return $"{method.ContainingType.ToDisplayString()}.{method.Name}";
        }

        protected void CheckContainingType(SyntaxNode node, SemanticModel model, List<SecurityIssue> issues, string targetTypeFullName)
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                var symbol = model.GetSymbolInfo(invocation).Symbol;
                if (symbol != null && symbol.ContainingType != null)
                {
                    var containingType = symbol.ContainingType.ToDisplayString();
                    if (containingType == targetTypeFullName)
                        Report(node, issues);
                }
            }

            if (node is MemberAccessExpressionSyntax memberAccess)
            {
                var symbol = model.GetSymbolInfo(memberAccess).Symbol;
                if (symbol != null && symbol.ContainingType != null)
                {
                    var containingType = symbol.ContainingType.ToDisplayString();
                    if (containingType == targetTypeFullName)
                        Report(node, issues);
                }
            }
        }
    }
}
