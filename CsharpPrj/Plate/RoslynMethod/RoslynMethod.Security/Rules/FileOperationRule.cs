using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Security
{
    public class FileOperationRule : SecurityRuleBase
    {
        public override string Id => "SEC001";
        public override string Title => "禁止文件系统写操作";
        public override string Description => "动态代码不允许创建、修改或删除文件。";

        private static readonly HashSet<string> ForbiddenMethods = new(StringComparer.Ordinal)
        {
            "System.IO.File.Delete",
            "System.IO.File.WriteAllText",
            "System.IO.File.WriteAllBytes",
            "System.IO.File.AppendAllText",
            "System.IO.File.Copy",
            "System.IO.File.Move",
            "System.IO.File.Replace",
            "System.IO.File.SetAttributes",
            "System.IO.Directory.Delete",
            "System.IO.Directory.CreateDirectory",
            "System.IO.Directory.Move",
            "System.IO.FileStream.Write",
            "System.IO.FileInfo.Delete",
            "System.IO.FileInfo.CopyTo",
            "System.IO.FileInfo.MoveTo",
            "System.IO.DirectoryInfo.Delete",
            "System.IO.DirectoryInfo.MoveTo"
        };

        private static readonly Dictionary<string, int[]> WriteParameterIndices = new(StringComparer.Ordinal)
        {
            ["System.IO.File.Delete"] = new int[] { 0 },
            ["System.IO.File.WriteAllText"] = new int[] { 0 },
            ["System.IO.File.WriteAllBytes"] = new int[] { 0 },
            ["System.IO.File.AppendAllText"] = new int[] { 0 },
            ["System.IO.File.Copy"] = new int[] { 1 },
            ["System.IO.File.Move"] = new int[] { 1 },
            ["System.IO.File.Replace"] = new int[] { 0, 1, 2 },
            ["System.IO.File.SetAttributes"] = new int[] { 0 },
            ["System.IO.Directory.Delete"] = new int[] { 0 },
            ["System.IO.Directory.CreateDirectory"] = new int[] { 0 },
            ["System.IO.Directory.Move"] = new int[] { 1 },
            ["System.IO.FileInfo.CopyTo"] = new int[] { 0 },
            ["System.IO.FileInfo.MoveTo"] = new int[] { 0 },
            ["System.IO.DirectoryInfo.MoveTo"] = new int[] { 0 }
        };

        public override void Analyze(SyntaxNode node, SemanticModel model, List<SecurityIssue> issues)
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (symbol == null) return;

                string fullName = GetMethodFullName(symbol);
                if (!ForbiddenMethods.Contains(fullName))
                    return;

                bool isWhitelisted = false;
                if (WriteParameterIndices.TryGetValue(fullName, out var indices))
                {
                    isWhitelisted = true;
                    foreach (int index in indices)
                    {
                        var arg = invocation.ArgumentList.Arguments.ElementAtOrDefault(index);
                        if (arg == null)
                        {
                            isWhitelisted = false;
                            break;
                        }

                        var constant = model.GetConstantValue(arg.Expression);
                        if (!constant.HasValue || !(constant.Value is string path))
                        {
                            isWhitelisted = false;
                            break;
                        }

                        if (!path.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                        {
                            isWhitelisted = false;
                            break;
                        }
                    }
                }
                else
                {
                    isWhitelisted = false;
                }

                if (!isWhitelisted)
                    Report(node, issues);
            }
        }

        private new string GetMethodFullName(IMethodSymbol method)
        {
            if (method.ContainingType == null)
                return method.Name;
            return $"{method.ContainingType.ToDisplayString()}.{method.Name}";
        }
    }
}
