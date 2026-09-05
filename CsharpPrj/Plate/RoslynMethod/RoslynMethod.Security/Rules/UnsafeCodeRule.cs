using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace RoslynSpace.RoslynSyntaxUtilities.Security
{
    public class UnsafeCodeRule : SecurityRuleBase
    {
        public override string Id => "SEC004";
        public override string Title => "禁止不安全代码";
        public override string Description => "动态代码不允许使用 unsafe 上下文或指针操作。";

        public override void Analyze(SyntaxNode node, SemanticModel model, List<SecurityIssue> issues)
        {
            if (node is UnsafeStatementSyntax)
                Report(node, issues);
            else if (node is MethodDeclarationSyntax method && method.Modifiers.Any(SyntaxKind.UnsafeKeyword))
                Report(node, issues);
            else if (node is ClassDeclarationSyntax cls && cls.Modifiers.Any(SyntaxKind.UnsafeKeyword))
                Report(node, issues);
            else if (node is PointerTypeSyntax)
                Report(node, issues);
        }
    }
}
