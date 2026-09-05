using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace RoslynSpace.RoslynSyntaxUtilities.Security
{
    public class ReflectionRule : SecurityRuleBase
    {
        public override string Id => "SEC003";
        public override string Title => "禁止危险的反射操作";
        public override string Description => "动态代码不允许动态加载程序集或通过反射调用方法。";

        private static readonly HashSet<string> ForbiddenMethods = new(System.StringComparer.Ordinal)
        {
            "System.Reflection.Assembly.Load",
            "System.Reflection.Assembly.LoadFrom",
            "System.Reflection.Assembly.LoadFile",
            "System.Activator.CreateInstance",
            "System.Type.GetMethod",
            "System.Reflection.MethodInfo.Invoke",
            "System.Reflection.ConstructorInfo.Invoke",
            "System.Reflection.FieldInfo.SetValue",
            "System.Reflection.PropertyInfo.SetValue",
        };

        public override void Analyze(SyntaxNode node, SemanticModel model, List<SecurityIssue> issues)
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (symbol == null) return;
                string fullName = GetMethodFullName(symbol);
                if (ForbiddenMethods.Contains(fullName))
                    Report(node, issues);
            }
        }
    }
}
