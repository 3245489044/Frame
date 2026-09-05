using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RoslynSpace.RoslynSyntaxHierarchy

{
    #region 函数层级分析模块
    public static class FunctionHierarchyAnalyzer
    {
        public static List<FunctionHierarchyInfo> AnalyzeFunctionHierarchy(AnalysisContext context, string className)
        {
            ArgumentNullException.ThrowIfNull(context);

            var methods = context.GetMethods(className).ToList();
            if (methods.Count == 0)
                throw new ArgumentException($"类 '{className}' 中没有方法");

            var classNode = context.GetClasses(className).FirstOrDefault();
            if (classNode == null)
                throw new ArgumentException($"类 '{className}' 未找到");


            var callGraph = BuildCallGraph(context, classNode, methods);

            return CalculateFunctionHierarchy(context, methods, callGraph);
        }

        private static CallGraphData BuildCallGraph(AnalysisContext context, SyntaxNode classNode, List<MethodDeclarationSyntax> methods)
        {
            var callGraph = new Dictionary<string, HashSet<string>>();
            var reverseGraph = new Dictionary<string, HashSet<string>>();
            var methodSignatures = new Dictionary<string, MethodData>();

            foreach (var method in methods)
            {
                var methodSymbol = SymbolAnalyzer.GetMethodSymbol(context.SemanticModel, method);
                var signature = SymbolAnalyzer.GetMethodSignature(methodSymbol);
                var methodData = new MethodData
                {
                    Method = method,
                    Symbol = methodSymbol,
                    Signature = signature,
                    MethodName = method.Identifier.Text,
                    ReturnType = method.ReturnType.ToString(),
                    Parameters = method.ParameterList.Parameters.Select(p => p.Identifier.Text).ToList(),
                    Accessibility = SyntaxAccessor.GetAccessibilityString(method.Modifiers),

                    IsStatic = method.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)),
                    LineNumber = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                };

                methodSignatures[signature] = methodData;
                callGraph[signature] = new HashSet<string>();
                reverseGraph[signature] = new HashSet<string>();
            }

            foreach (var method in methods)
            {
                var methodSymbol = SymbolAnalyzer.GetMethodSymbol(context.SemanticModel, method);
                var callerSignature = SymbolAnalyzer.GetMethodSignature(methodSymbol);

                var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();
                foreach (var invocation in invocations)
                {
                    var calledSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                    if (calledSymbol != null)
                    {
                        var calleeSignature = SymbolAnalyzer.GetMethodSignature(calledSymbol);
                        if (methodSignatures.ContainsKey(calleeSignature))
                        {
                            callGraph[callerSignature].Add(calleeSignature);
                            reverseGraph[calleeSignature].Add(callerSignature);
                        }
                    }
                }
            }

            return new CallGraphData
            {
                CallGraph = callGraph,
                ReverseGraph = reverseGraph,
                MethodSignatures = methodSignatures
            };
        }

        private static List<FunctionHierarchyInfo> CalculateFunctionHierarchy(AnalysisContext context,
            List<MethodDeclarationSyntax> methods, CallGraphData graphData)
        {
            var hierarchy = new List<FunctionHierarchyInfo>();
            var allSignatures = graphData.MethodSignatures.Keys.ToList();
            var inDegree = new Dictionary<string, int>();
            var levelMap = new Dictionary<string, int>();

            foreach (var sig in allSignatures)
            {
                inDegree[sig] = 0;
            }

            foreach (var sig in allSignatures)
            {
                if (graphData.CallGraph.ContainsKey(sig))
                {
                    foreach (var callee in graphData.CallGraph[sig])
                    {
                        if (inDegree.ContainsKey(callee))
                        {
                            inDegree[callee]++;
                        }
                    }
                }
            }

            var visited = new HashSet<string>();

            var rootFunctions = allSignatures
                .Where(sig => inDegree[sig] == 0)
                .ToList();

            foreach (var root in rootFunctions)
            {
                CalculateDepth(root, graphData.CallGraph, levelMap, visited);
            }

            foreach (var signature in allSignatures)
            {
                if (!levelMap.ContainsKey(signature))
                {
                    if (HasCircularDependency(signature, graphData.CallGraph, new HashSet<string>()))
                    {
                        levelMap[signature] = 1;
                    }
                    else
                    {
                        levelMap[signature] = 0;
                    }
                }
            }

            var maxLevel = levelMap.Values.Any() ? levelMap.Values.Max() : 0;

            if (maxLevel > 0)
            {
                var minLevel = levelMap.Values.Where(v => v > 0).DefaultIfEmpty(0).Min();
                if (minLevel > 1)
                {
                    foreach (var key in levelMap.Keys.ToList())
                    {
                        if (levelMap[key] > 0)
                            levelMap[key] = levelMap[key] - minLevel + 1;
                    }
                }
            }

            foreach (var signature in allSignatures)
            {
                if (graphData.MethodSignatures.TryGetValue(signature, out var methodData))
                {
                    var info = CreateFunctionHierarchyInfo(methodData, graphData, levelMap, inDegree);
                    hierarchy.Add(info);
                }
            }

            return hierarchy.OrderBy(h => h.Level)
                           .ThenBy(h => h.FunctionName)
                           .ToList();
        }

        private static int CalculateDepth(string signature,
            Dictionary<string, HashSet<string>> callGraph,
            Dictionary<string, int> levelMap,
            HashSet<string> visited)
        {
            if (levelMap.ContainsKey(signature))
                return levelMap[signature];

            if (visited.Contains(signature))
                return 0;

            visited.Add(signature);

            int maxDepth = 0;
            if (callGraph.ContainsKey(signature))
            {
                foreach (var callee in callGraph[signature])
                {
                    var depth = CalculateDepth(callee, callGraph, levelMap, visited);
                    maxDepth = Math.Max(maxDepth, depth);
                }
            }

            visited.Remove(signature);

            var resultDepth = maxDepth == 0 ? 1 : maxDepth + 1;
            levelMap[signature] = resultDepth;

            return resultDepth;
        }

        private static bool HasCircularDependency(string signature,
            Dictionary<string, HashSet<string>> callGraph,
            HashSet<string> visited)
        {
            if (visited.Contains(signature))
                return true;

            if (!callGraph.ContainsKey(signature))
                return false;

            visited.Add(signature);

            foreach (var callee in callGraph[signature])
            {
                if (HasCircularDependency(callee, callGraph, visited))
                {
                    visited.Remove(signature);
                    return true;
                }
            }

            visited.Remove(signature);
            return false;
        }

        private static FunctionHierarchyInfo CreateFunctionHierarchyInfo(MethodData methodData,
            CallGraphData graphData,
            Dictionary<string, int> levelMap,
            Dictionary<string, int> inDegree)
        {
            var signature = methodData.Signature;
            var calledFunctions = graphData.CallGraph.ContainsKey(signature)
                ? graphData.CallGraph[signature].ToList()
                : new List<string>();

            var callers = graphData.ReverseGraph.ContainsKey(signature)
                ? graphData.ReverseGraph[signature].ToList()
                : new List<string>();

            var level = levelMap.GetValueOrDefault(signature, 0);

            return new FunctionHierarchyInfo
            {
                FunctionSignature = signature,
                FunctionName = methodData.MethodName,
                Level = level,
                IsTopLevel = level == 1,
                IsLeafFunction = calledFunctions.Count == 0,
                Accessibility = methodData.Accessibility,
                IsStatic = methodData.IsStatic,
                ReturnType = methodData.ReturnType,
                Parameters = methodData.Parameters,
                LineNumber = methodData.LineNumber,
                CalledFunctions = calledFunctions,
                Callers = callers,
                CallerCount = callers.Count,
                ComplexityScore = CalculateMethodComplexity(methodData.Parameters.Count, methodData.IsStatic, calledFunctions.Count),

                HasCircularDependency = HasCircularDependency(signature, graphData.CallGraph, new HashSet<string>())
            };
        }

        public static LevelStatistics GetLevelStatistics(List<FunctionHierarchyInfo> hierarchy)
        {
            if (hierarchy == null || hierarchy.Count == 0)
                return new LevelStatistics();

            return new LevelStatistics
            {
                FunctionCount = hierarchy.Count,
                AverageCallers = hierarchy.Average(h => h.CallerCount),
                HasCircularDependency = hierarchy.Any(h => h.HasCircularDependency),
                TopLevelFunctions = hierarchy.Count(h => h.IsTopLevel),
                LeafFunctions = hierarchy.Count(h => h.IsLeafFunction)
            };
        }

        public static List<List<string>> FindCircularDependencies(this List<FunctionHierarchyInfo> hierarchy)
        {
            var adjacency = hierarchy.ToDictionary(
                f => f.FunctionSignature,
                f => f.CalledFunctions);

            return FindCycles(adjacency);
        }

        private static int CalculateMethodComplexity(int parameterCount, bool isStatic, int calledFunctionCount = 0)
        {
            int complexity = 1;

            complexity += calledFunctionCount / 2;

            complexity += parameterCount / 2;

            if (isStatic) complexity += 1;

            return Math.Clamp(complexity, 1, 10);
        }

        private static List<List<string>> FindCycles(Dictionary<string, List<string>> adjacency)
        {
            var cycles = new List<List<string>>();
            var visited = new HashSet<string>();
            var path = new Stack<string>();

            foreach (var node in adjacency.Keys)
            {
                FindCyclesDFS(node, adjacency, visited, new HashSet<string>(), path, cycles);
            }

            return cycles;
        }

        private static void FindCyclesDFS(string current,
            Dictionary<string, List<string>> adjacency,
            HashSet<string> visited,
            HashSet<string> recursionStack,
            Stack<string> path,
            List<List<string>> cycles)
        {
            if (!adjacency.ContainsKey(current))
                return;

            if (recursionStack.Contains(current))
            {
                var pathArray = path.ToArray();
                var cycle = new List<string> { current };
                foreach (var node in pathArray)
                {
                    cycle.Add(node);
                    if (node == current)
                        break;
                }
                cycle.Reverse();
                cycles.Add(cycle);
                return;
            }

            if (visited.Contains(current))
                return;

            visited.Add(current);
            recursionStack.Add(current);
            path.Push(current);

            foreach (var neighbor in adjacency[current])
            {
                FindCyclesDFS(neighbor, adjacency, visited, recursionStack, path, cycles);
            }

            path.Pop();
            recursionStack.Remove(current);
        }
    }

    public class LevelStatistics
    {
        public int FunctionCount { get; set; }
        public double AverageCallers { get; set; }
        public bool HasCircularDependency { get; set; }
        public int TopLevelFunctions { get; set; }
        public int LeafFunctions { get; set; }
    }
    #endregion

    #region 函数层级信息模型
        public class FunctionHierarchyInfo
    {
        public string FunctionSignature { get; set; }
        public string FunctionName { get; set; }
        public int Level { get; set; }
        public bool IsTopLevel { get; set; }
        public bool IsLeafFunction { get; set; }
        public string Accessibility { get; set; }
        public bool IsStatic { get; set; }
        public string ReturnType { get; set; }
        public List<string> Parameters { get; set; } = new();
        public int LineNumber { get; set; }
        public List<string> CalledFunctions { get; set; } = new();
        public List<string> Callers { get; set; } = new();
        public int CallerCount { get; set; }
        public int ComplexityScore { get; set; }
        public bool HasCircularDependency { get; set; }
    }

    public class CallGraphData
    {
        public Dictionary<string, HashSet<string>> CallGraph { get; set; } = new();
        public Dictionary<string, HashSet<string>> ReverseGraph { get; set; } = new();
        public Dictionary<string, MethodData> MethodSignatures { get; set; } = new();
    }

    public class MethodData
    {
        public MethodDeclarationSyntax Method { get; set; }
        public IMethodSymbol Symbol { get; set; }
        public string Signature { get; set; }
        public string MethodName { get; set; }
        public string ReturnType { get; set; }
        public List<string> Parameters { get; set; } = new();
        public string Accessibility { get; set; }
        public bool IsStatic { get; set; }
        public int LineNumber { get; set; }
    }

    public class HierarchyExport
    {
        public string Content { get; set; }
        public string Format { get; set; }
    }
    #endregion

    #region 函数关系图可视化工具
    public static class FunctionGraphVisualizer
    {
        public static string ToDotGraph(List<FunctionHierarchyInfo> hierarchy)
        {
            var sb = new StringBuilder();
            sb.AppendLine("digraph FunctionHierarchy {");
            sb.AppendLine("  rankdir=TB;");
            sb.AppendLine("  node [shape=box, style=filled];");
            sb.AppendLine("  edge [arrowhead=vee];");

            var levels = hierarchy.GroupBy(h => h.Level).OrderBy(g => g.Key);

            foreach (var level in levels)
            {
                sb.AppendLine($"  subgraph cluster_L{level.Key} {{");
                sb.AppendLine($"    label = \"层级 {level.Key}\";");
                sb.AppendLine($"    style = filled;");
                sb.AppendLine($"    fillcolor = \"{GetLevelColor(level.Key)}\";");

                foreach (var info in level)
                {
                    var label = FormatNodeLabel(info);
                    sb.AppendLine($"    \"{info.FunctionSignature}\" [");
                    sb.AppendLine($"      label=\"{label}\"");
                    sb.AppendLine($"      fillcolor=\"white\"");
                    sb.AppendLine($"      style=\"filled,rounded\"");
                    sb.AppendLine($"    ];");
                }

                sb.AppendLine("  }");
            }

            foreach (var info in hierarchy)
            {
                foreach (var calledFunc in info.CalledFunctions)
                {
                    sb.AppendLine($"  \"{info.FunctionSignature}\" -> \"{calledFunc}\";");
                }
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string ToJson(List<FunctionHierarchyInfo> hierarchy)
        {
            var nodes = hierarchy.Select(info => new
            {
                id = info.FunctionSignature,
                name = info.FunctionName,
                level = info.Level,
                isTopLevel = info.IsTopLevel,
                isLeaf = info.IsLeafFunction,
                accessibility = info.Accessibility,
                isStatic = info.IsStatic,
                returnType = info.ReturnType,
                parameters = info.Parameters,
                line = info.LineNumber,
                complexity = info.ComplexityScore,
                callerCount = info.CallerCount
            }).ToList();

            var edges = hierarchy
                .Where(h => h.CalledFunctions.Any())
                .SelectMany(h => h.CalledFunctions
                    .Select(cf => new
                    {
                        source = h.FunctionSignature,
                        target = cf
                    }))
                .ToList();

            var result = new
            {
                metadata = new
                {
                    generatedAt = DateTime.UtcNow,
                    totalFunctions = hierarchy.Count,
                    maxLevel = hierarchy.Max(h => h.Level),
                    hasCircularDependency = edges.Any(e => edges.Any(e2 =>
                        e2.source == e.target && e2.target == e.source))
                },
                nodes,
                edges
            };

            return System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        public static HierarchyExport Export(List<FunctionHierarchyInfo> hierarchy, string format = "dot")
        {
            return format?.ToLower() switch
            {
                "dot" => new HierarchyExport { Content = ToDotGraph(hierarchy), Format = "dot" },
                "json" => new HierarchyExport { Content = ToJson(hierarchy), Format = "json" },
                _ => throw new ArgumentException($"不支持的导出格式: {format}。支持 'dot' 和 'json'。")
            };
        }

        private static string FormatNodeLabel(FunctionHierarchyInfo info)
        {
            var sb = new StringBuilder();
            sb.Append(info.Accessibility == "public" ? "+ " : "- ");
            if (info.IsStatic) sb.Append("static ");
            sb.Append(info.ReturnType);
            sb.Append(" ");
            sb.Append(info.FunctionName);
            sb.Append("(");
            sb.Append(string.Join(", ", info.Parameters));
            sb.Append(")");
            sb.Append($"\\n[层级: {info.Level}]");
            if (info.CallerCount > 0)
                sb.Append($" | 被调用: {info.CallerCount}次");
            if (info.ComplexityScore > 0)
                sb.Append($" | 复杂度: {info.ComplexityScore}");
            return sb.ToString();
        }

        private static string GetLevelColor(int level)
        {
            return level switch
            {
                1 => "#E8F5E9",
                2 => "#C8E6C9",
                3 => "#A5D6A7",
                4 => "#81C784",
                5 => "#66BB6A",
                _ => "#E8F5E9"
            };
        }
    }
    #endregion
}
