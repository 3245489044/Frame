using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using RoslynSpace.RoslynSyntaxUtilities.Core.Utilities;

namespace CodeRLFramework.VectorSpace
{
    public class VectorExtractor
    {

        public double[] ExtractSemanticVector(MethodDeclarationSyntax method)
        {

            var vector = new double[250];

            string name = method.Identifier.Text;
            for (int i = 0; i < name.Length; i++)
            {
                int idx = Math.Abs(name[i].GetHashCode()) % 50;
                vector[idx] += 1.0 / name.Length;
            }

            var paramTypes = method.ParameterList.Parameters
                .Select(p => p.Type?.ToString() ?? "var")
                .ToList();
            for (int i = 0; i < paramTypes.Count && i < 10; i++)
            {
                int idx = 50 + (Math.Abs(paramTypes[i].GetHashCode()) % 50);
                vector[idx] += 1.0;
            }

            string returnType = method.ReturnType.ToString();
            int retIdx = 100 + (Math.Abs(returnType.GetHashCode()) % 50);
            vector[retIdx] = 1.0;

            var ns = GetNamespace(method);
            if (!string.IsNullOrEmpty(ns))
            {
                int nsIdx = 150 + (Math.Abs(ns.GetHashCode()) % 50);
                vector[nsIdx] = 1.0;
            }

            foreach (var modifier in method.Modifiers)
            {
                int modIdx = 200 + (Math.Abs(modifier.Text.GetHashCode()) % 50);
                vector[modIdx] = 1.0;
            }

            return vector;
        }

        public double[] ExtractStructuralVector(MethodDeclarationSyntax method)
        {

            var vector = new double[40];

            var statements = method.DescendantNodes().OfType<StatementSyntax>().ToList();
            foreach (var stmt in statements)
            {
                int idx = stmt.Kind() switch
                {
                    SyntaxKind.ReturnStatement => 0,
                    SyntaxKind.IfStatement => 1,
                    SyntaxKind.ForStatement or SyntaxKind.ForEachStatement => 2,
                    SyntaxKind.WhileStatement or SyntaxKind.DoStatement => 3,
                    SyntaxKind.SwitchStatement => 4,
                    SyntaxKind.TryStatement => 5,
                    SyntaxKind.UsingStatement => 6,
                    SyntaxKind.LocalDeclarationStatement => 7,
                    SyntaxKind.ExpressionStatement => 8,
                    _ => 9
                };
                vector[idx] += 1.0 / statements.Count;
            }

            vector[10] = CalculateCyclomaticComplexity(method) / 100.0;
            vector[11] = method.DescendantNodes().OfType<IfStatementSyntax>().Count() / 20.0;
            vector[12] = method.DescendantNodes().OfType<ForStatementSyntax>().Count()
                       + method.DescendantNodes().OfType<ForEachStatementSyntax>().Count()
                       + method.DescendantNodes().OfType<WhileStatementSyntax>().Count()
                       + method.DescendantNodes().OfType<DoStatementSyntax>().Count();
            vector[12] = Math.Min(vector[12] / 10.0, 1.0);
            vector[13] = method.DescendantNodes().OfType<TryStatementSyntax>().Count() / 5.0;

            var expressions = method.DescendantNodes().OfType<ExpressionSyntax>().ToList();
            foreach (var expr in expressions)
            {
                int idx = 20 + (expr.Kind() switch
                {
                    SyntaxKind.InvocationExpression => 0,
                    SyntaxKind.SimpleMemberAccessExpression => 1,
                    SyntaxKind.AddExpression or SyntaxKind.SubtractExpression
                        or SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression => 2,
                    SyntaxKind.UnaryMinusExpression or SyntaxKind.UnaryPlusExpression
                        or SyntaxKind.LogicalNotExpression => 3,
                    SyntaxKind.ConditionalExpression => 4,
                    SyntaxKind.SimpleLambdaExpression or SyntaxKind.ParenthesizedLambdaExpression => 5,
                    SyntaxKind.ObjectCreationExpression => 6,
                    _ => 7
                });
                if (idx < 30) vector[idx] += 1.0 / Math.Max(expressions.Count, 1);
            }

            vector[30] = Math.Min(method.ToString().Length / 1000.0, 1.0);
            vector[31] = Math.Min(method.DescendantNodes().OfType<IdentifierNameSyntax>().Count() / 100.0, 1.0);
            vector[32] = method.ParameterList.Parameters.Count / 10.0;

            return vector;
        }

        public double[] ExtractRelationVector(
            MethodDeclarationSyntax method,
            Dictionary<string, List<string>> callGraph)
        {
            var vector = new double[50];
            string methodName = method.Identifier.Text;

            if (callGraph.TryGetValue(methodName, out var callees))
            {
                foreach (var callee in callees)
                {
                    int idx = Math.Abs(callee.GetHashCode()) % 25;
                    vector[idx] += 1.0;
                }
            }

            foreach (var kvp in callGraph)
            {
                if (kvp.Value.Contains(methodName))
                {
                    int idx = 25 + (Math.Abs(kvp.Key.GetHashCode()) % 25);
                    vector[idx] += 1.0;
                }
            }

            return vector;
        }

        public FunctionVectorSpace ExtractFromCode(string code, string source = "codebase")
        {
            var space = new FunctionVectorSpace
            {
                Source = source,
                VectorDimension = 340,
                Weight = source == "codebase" ? 0.7 : 0.3
            };

            try
            {
                var elements = SyntaxElementExtractor.Extract(code);

                var tree = CSharpSyntaxTree.ParseText(code);
                var root = tree.GetRoot();

                var callGraph = BuildCallGraph(root);

                var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
                foreach (var method in methods)
                {
                    var element = elements.FirstOrDefault(e => e.Name == method.Identifier.Text);

                    var funcVector = new FunctionVector
                    {
                        Id = $"{source}_{method.Identifier.Text}_{method.Span.Start}",
                        Name = method.Identifier.Text,
                        Signature = GetMethodSignature(method),
                        ClassName = GetClassName(method),
                        Namespace = GetNamespace(method),
                        ReturnType = method.ReturnType.ToString(),
                        Parameters = method.ParameterList.Parameters
                            .Select(p => new ParameterInfo
                            {
                                Name = p.Identifier.Text,
                                Type = p.Type?.ToString() ?? "var"
                            }).ToList(),
                        ComplexityScore = element?.ComplexityScore ?? 0,
                        IsPure = element?.IsPure ?? false,
                        HasSideEffects = element?.HasSideEffects ?? true,
                        SourceCode = method.ToString(),
                        Source = source,
                        SourceWeight = space.Weight,
                        SemanticVector = ExtractSemanticVector(method),
                        StructuralVector = ExtractStructuralVector(method),
                        RelationVector = ExtractRelationVector(method, callGraph)
                    };

                    if (element != null)
                    {
                        funcVector.Calls.AddRange(element.References.Keys);
                        funcVector.CalledBy.AddRange(element.ReferencedBy);
                    }

                    space.Functions.Add(funcVector);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"向量提取失败: {ex.Message}");
            }

            return space;
        }

        public FunctionVectorSpace ExtractFromMultiple(List<string> codes, string source = "codebase")
        {
            var merged = new FunctionVectorSpace
            {
                Source = source,
                VectorDimension = 340,
                Weight = source == "codebase" ? 0.7 : 0.3
            };

            foreach (var code in codes)
            {
                var space = ExtractFromCode(code, source);
                foreach (var func in space.Functions)
                {
                    if (!merged.Functions.Any(f => f.Name == func.Name && f.ClassName == func.ClassName))
                    {
                        merged.Functions.Add(func);
                    }
                }
            }

            return merged;
        }


        private static string GetMethodSignature(MethodDeclarationSyntax method)
        {
            var returnType = method.ReturnType.ToString();
            var name = method.Identifier.Text;
            var parameters = string.Join(", ",
                method.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier.Text}"));
            return $"{returnType} {name}({parameters})";
        }

        private static string GetClassName(MethodDeclarationSyntax method)
        {
            var classDecl = method.Ancestors()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault();
            return classDecl?.Identifier.Text ?? "";
        }

        private static string GetNamespace(MethodDeclarationSyntax method)
        {
            var nsDecl = method.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .FirstOrDefault();
            return nsDecl?.Name?.ToString() ?? "";
        }

        private static int CalculateCyclomaticComplexity(MethodDeclarationSyntax method)
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

        private static Dictionary<string, List<string>> BuildCallGraph(SyntaxNode root)
        {
            var graph = new Dictionary<string, List<string>>();
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var method in methods)
            {
                var name = method.Identifier.Text;
                if (!graph.ContainsKey(name))
                    graph[name] = new List<string>();

                var invocations = method.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Select(inv => (inv.Expression as MemberAccessExpressionSyntax)
                        ?.Name?.Identifier.Text ??
                        (inv.Expression as IdentifierNameSyntax)?.Identifier.Text)
                    .Where(x => x != null);

                foreach (var callee in invocations)
                {
                    if (!graph[name]!.Contains(callee!))
                        graph[name]!.Add(callee!);
                }
            }

            return graph;
        }
    }
}
