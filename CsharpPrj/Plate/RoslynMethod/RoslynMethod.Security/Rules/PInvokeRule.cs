using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Security
{
    public class PInvokeRule : SecurityRuleBase
    {
        public override string Id => "SEC008";
        public override string Title => "禁止平台调用 (P/Invoke)";
        public override string Description => "动态代码不允许声明外部DLL函数。";

        public override void Analyze(SyntaxNode node, SemanticModel model, List<SecurityIssue> issues)
        {
            if (node is MethodDeclarationSyntax method)
            {
                var dllImportType = model.Compilation.GetTypeByMetadataName("System.Runtime.InteropServices.DllImportAttribute");
                if (dllImportType == null) return;

                foreach (var attrList in method.AttributeLists)
                {
                    foreach (var attr in attrList.Attributes)
                    {
                        var attrSymbol = model.GetSymbolInfo(attr).Symbol as IMethodSymbol;
                        if (attrSymbol == null) continue;

                        if (SymbolEqualityComparer.Default.Equals(attrSymbol.ContainingType, dllImportType))
                        {
                            Report(node, issues);
                            return;
                        }
                    }
                }
            }
        }
    }
}
