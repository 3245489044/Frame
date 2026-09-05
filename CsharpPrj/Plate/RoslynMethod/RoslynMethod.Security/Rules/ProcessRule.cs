using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace RoslynSpace.RoslynSyntaxUtilities.Security
{
    public class ProcessRule : SecurityRuleBase
    {
        public override string Id => "SEC002";
        public override string Title => "禁止启动新进程";
        public override string Description => "动态代码不允许启动外部程序。";

        public override void Analyze(SyntaxNode node, SemanticModel model, List<SecurityIssue> issues)
        {
            if (node is ObjectCreationExpressionSyntax creation)
            {
                var type = model.GetTypeInfo(creation).Type;
                if (type == null) return;

                var processType = model.Compilation.GetTypeByMetadataName("System.Diagnostics.Process");
                if (processType != null && IsDerivedFrom(type, processType))
                    Report(node, issues);
            }

            if (node is InvocationExpressionSyntax invocation)
            {
                var symbol = model.GetSymbolInfo(invocation).Symbol;
                if (symbol == null) return;

                var processType = model.Compilation.GetTypeByMetadataName("System.Diagnostics.Process");
                if (processType != null &&
                    symbol.ContainingType != null &&
                    symbol.Name == "Start" &&
                    IsDerivedFrom(symbol.ContainingType, processType))
                {
                    Report(node, issues);
                }
            }
        }
    }
}
