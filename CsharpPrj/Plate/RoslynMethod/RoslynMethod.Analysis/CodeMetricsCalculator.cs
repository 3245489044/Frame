using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    internal static class ComplexityCalculator
    {
        public static int CalculateMethodComplexity(MethodDeclarationSyntax method)
        {
            int complexity = 1;
            complexity += method.DescendantNodes().OfType<IfStatementSyntax>().Count();
            complexity += method.DescendantNodes().OfType<WhileStatementSyntax>().Count();
            complexity += method.DescendantNodes().OfType<ForStatementSyntax>().Count();
            complexity += method.DescendantNodes().OfType<ForEachStatementSyntax>().Count();
            complexity += method.DescendantNodes().OfType<SwitchStatementSyntax>().Count();
            complexity += method.DescendantNodes().OfType<CatchClauseSyntax>().Count();
            complexity += method.DescendantNodes().OfType<ConditionalExpressionSyntax>().Count();
            return complexity;
        }

        public static int CalculateConstructorComplexity(ConstructorDeclarationSyntax constructor)
        {
            int complexity = 1;
            complexity += constructor.DescendantNodes().OfType<IfStatementSyntax>().Count();
            complexity += constructor.DescendantNodes().OfType<WhileStatementSyntax>().Count();
            complexity += constructor.DescendantNodes().OfType<ForStatementSyntax>().Count();
            complexity += constructor.DescendantNodes().OfType<ForEachStatementSyntax>().Count();
            return complexity;
        }
    }

    internal static class VariableMetricsCalculator
    {
        public static double CalculateVariableReuseRatio(int reusedVariables, int totalVariables)
        {
            if (totalVariables == 0) return 0;
            return Math.Round((double)reusedVariables / totalVariables, 2);
        }

        public static double CalculateVariableScopeComplexity(List<VariableUsageInfo> variables)
        {
            if (variables == null || variables.Count == 0) return 0;

            double totalComplexity = 0;
            foreach (var variable in variables)
            {
                if (variable.UsageLines.Count > 0)
                {
                    var minLine = variable.UsageLines.Min();
                    var maxLine = variable.UsageLines.Max();
                    var scopeSpan = maxLine - minLine;
                    totalComplexity += Math.Log(scopeSpan + 1, 2);
                }
            }

            return Math.Round(totalComplexity / variables.Count, 2);
        }
    }
}
