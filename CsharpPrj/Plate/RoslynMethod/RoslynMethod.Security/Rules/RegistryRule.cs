using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace RoslynSpace.RoslynSyntaxUtilities.Security
{
    public class RegistryRule : SecurityRuleBase
    {
        public override string Id => "SEC007";
        public override string Title => "禁止访问注册表";
        public override string Description => "动态代码不允许读写注册表。";

        public override void Analyze(SyntaxNode node, SemanticModel model, List<SecurityIssue> issues)
        {
            CheckContainingType(node, model, issues, "Microsoft.Win32.Registry");
        }
    }
}
