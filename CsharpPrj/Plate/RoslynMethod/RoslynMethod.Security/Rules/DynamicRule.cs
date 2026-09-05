using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace RoslynSpace.RoslynSyntaxUtilities.Security
{
    public class DynamicRule : SecurityRuleBase
    {
        public override string Id => "SEC005";
        public override string Title => "禁止使用 dynamic 类型";
        public override string Description => "dynamic 可能绕过安全检查，请使用具体类型。";

        public override void Analyze(SyntaxNode node, SemanticModel model, List<SecurityIssue> issues)
        {
            if (node is IdentifierNameSyntax identifier && identifier.Identifier.Text == "dynamic")
            {
                var typeInfo = model.GetTypeInfo(identifier);
                if (typeInfo.Type?.Kind == SymbolKind.DynamicType)
                    Report(node, issues);
            }
        }
    }
}
