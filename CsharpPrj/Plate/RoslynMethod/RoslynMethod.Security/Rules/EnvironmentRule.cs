using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace RoslynSpace.RoslynSyntaxUtilities.Security
{
    public class EnvironmentRule : SecurityRuleBase
    {
        public override string Id => "SEC006";
        public override string Title => "禁止访问系统环境信息";
        public override string Description => "动态代码不允许读取或修改环境变量、当前目录等。";

        public override void Analyze(SyntaxNode node, SemanticModel model, List<SecurityIssue> issues)
        {
            CheckContainingType(node, model, issues, "System.Environment");
        }
    }
}
