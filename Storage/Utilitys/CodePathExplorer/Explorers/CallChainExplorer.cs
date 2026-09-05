using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodePathExplorer.Models;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using RoslynSpace.RoslynSyntaxUtilities.Core.Utilities;

namespace CodePathExplorer.Explorers;

public class CallChainExplorer
{
    private readonly AnalysisContext _context;
    private readonly Dictionary<string, MethodDeclarationSyntax> _allMethods = new();
    private readonly HashSet<string> _builtInMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "ToString", "GetHashCode", "Equals", "GetType", "Dispose"
    };

    private int _chainCounter;

    public CallChainExplorer(AnalysisContext context)
    {
        _context = context;
        CollectAllMethods();
    }

    private void CollectAllMethods()
    {
        var methods = _context.Root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        foreach (var method in methods)
        {
            var className = GetContainingClassName(method);
            var key = $"{className}.{method.Identifier.Text}";
            if (!_allMethods.ContainsKey(key))
                _allMethods[key] = method;
        }
    }

    public List<CallChain> ExploreAll()
    {
        var allChains = new List<CallChain>();
        _chainCounter = 0;

        var entryMethods = _allMethods
            .Where(kv => IsPublicOrInternal(kv.Value))
            .ToList();

        foreach (var (fullName, method) in entryMethods)
        {
            var chains = ExploreFromMethod(fullName, method);
            allChains.AddRange(chains);
        }

        return allChains;
    }

    public List<CallChain> ExploreFromMethod(string entryFullName, MethodDeclarationSyntax? entryMethod = null)
    {
        var chains = new List<CallChain>();
        var currentPath = new List<CallChainNode>();

        if (entryMethod == null && _allMethods.TryGetValue(entryFullName, out var found))
            entryMethod = found;

        if (entryMethod == null) return chains;

        ExploreRecursive(entryFullName, entryMethod, currentPath, chains, new HashSet<string>());
        return chains;
    }

    private bool IsBuiltIn(string methodName) => _builtInMethods.Contains(methodName);

    private void ExploreRecursive(
        string methodFullName,
        MethodDeclarationSyntax method,
        List<CallChainNode> currentPath,
        List<CallChain> chains,
        HashSet<string> visited)
    {
        if (visited.Contains(methodFullName))
        {
            var chain = CreateChainFromPath(currentPath);
            chain.Nodes.Add(new CallChainNode
            {
                MethodFullName = methodFullName,
                ClassName = GetContainingClassName(method),
                MethodName = method.Identifier.Text,
                LineNumber = SyntaxHelper.GetLineNumber(method),
                IsLoop = true,
                LoopDescription = $"[循环回调用] {methodFullName}"
            });
            chains.Add(chain);
            return;
        }

        var node = new CallChainNode
        {
            MethodFullName = methodFullName,
            ClassName = GetContainingClassName(method),
            MethodName = method.Identifier.Text,
            LineNumber = SyntaxHelper.GetLineNumber(method)
        };

        currentPath.Add(node);
        visited.Add(methodFullName);

        var invocations = method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv => !IsInsideNestedLambdaOrLocalFunction(inv, method))
            .ToList();

        if (invocations.Count == 0)
        {
            chains.Add(CreateChainFromPath(currentPath));
        }
        else
        {
            var branches = AnalyzeTopLevelBranches(method);

            if (branches.Count > 0)
            {
                ProcessBranches(branches, currentPath, chains, visited);
            }
            else
            {
                ProcessSequentialCalls(invocations, currentPath, chains, visited);
            }
        }

        currentPath.RemoveAt(currentPath.Count - 1);
    }

    private static bool IsInsideNestedLambdaOrLocalFunction(InvocationExpressionSyntax invocation, MethodDeclarationSyntax method)
    {
        var lambdaParent = invocation.Ancestors()
            .FirstOrDefault(a => a is LambdaExpressionSyntax || a is AnonymousMethodExpressionSyntax || a is LocalFunctionStatementSyntax);
        if (lambdaParent != null)
        {
            var methodParent = lambdaParent.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            return methodParent == method;
        }
        return false;
    }

    private void ProcessBranches(
        List<BranchInfo> branches,
        List<CallChainNode> currentPath,
        List<CallChain> chains,
        HashSet<string> visited)
    {
        foreach (var branch in branches)
        {
            var branchPath = new List<CallChainNode>(currentPath);

            if (branchPath.Count > 0)
            {
                branchPath[^1].IsBranchPoint = true;
                branchPath[^1].BranchCondition = branch.Condition;
            }

            ProcessInvocationsInPath(branch.Invocations, branchPath, chains, visited);

            if (branch.Invocations.Count == 0)
            {
                chains.Add(CreateChainFromPath(branchPath));
            }
        }
    }

    private void ProcessSequentialCalls(
        List<InvocationExpressionSyntax> invocations,
        List<CallChainNode> currentPath,
        List<CallChain> chains,
        HashSet<string> visited)
    {
        bool hasInternalCall = false;

        foreach (var call in invocations)
        {
            var targetMethod = ResolveMethodTarget(call);
            if (targetMethod == null || IsBuiltIn(targetMethod.Value.methodName))
                continue;

            var targetFullName = $"{targetMethod.Value.className}.{targetMethod.Value.methodName}";

            if (_allMethods.TryGetValue(targetFullName, out var targetMethodDecl))
            {
                hasInternalCall = true;
                ExploreRecursive(targetFullName, targetMethodDecl, currentPath, chains, new HashSet<string>(visited));
            }
            else
            {
                currentPath.Add(new CallChainNode
                {
                    MethodFullName = targetFullName,
                    ClassName = targetMethod.Value.className,
                    MethodName = targetMethod.Value.methodName,
                    LineNumber = SyntaxHelper.GetLineNumber(call)
                });
            }
        }

        if (!hasInternalCall)
        {
            chains.Add(CreateChainFromPath(currentPath));
        }
    }

    private void ProcessInvocationsInPath(
        List<InvocationExpressionSyntax> invocations,
        List<CallChainNode> path,
        List<CallChain> chains,
        HashSet<string> visited)
    {
        bool hasInternalCall = false;

        foreach (var call in invocations)
        {
            var targetMethod = ResolveMethodTarget(call);
            if (targetMethod == null || IsBuiltIn(targetMethod.Value.methodName))
                continue;

            var targetFullName = $"{targetMethod.Value.className}.{targetMethod.Value.methodName}";

            if (_allMethods.TryGetValue(targetFullName, out var targetMethodDecl))
            {
                hasInternalCall = true;
                ExploreRecursive(targetFullName, targetMethodDecl, path, chains, new HashSet<string>(visited));
            }
            else
            {
                path.Add(new CallChainNode
                {
                    MethodFullName = targetFullName,
                    ClassName = targetMethod.Value.className,
                    MethodName = targetMethod.Value.methodName,
                    LineNumber = SyntaxHelper.GetLineNumber(call)
                });
            }
        }

        if (!hasInternalCall && invocations.Count > 0)
        {
            chains.Add(CreateChainFromPath(path));
        }
    }

    private List<BranchInfo> AnalyzeTopLevelBranches(MethodDeclarationSyntax method)
    {
        var branches = new List<BranchInfo>();

        var body = method.Body ?? (SyntaxNode?)method.ExpressionBody;
        if (body == null) return branches;

        var topLevelIfs = body.ChildNodes().OfType<IfStatementSyntax>().ToList();
        foreach (var ifStmt in topLevelIfs)
        {
            CollectIfBranches(ifStmt, branches);
        }

        var topLevelSwitches = body.ChildNodes().OfType<SwitchStatementSyntax>().ToList();
        foreach (var switchStmt in topLevelSwitches)
        {
            foreach (var section in switchStmt.Sections)
            {
                var labels = section.Labels;
                var labelText = labels.FirstOrDefault()?.ToString() ?? "default";
                var invocations = section.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Where(inv => !IsInsideNestedLambdaOrLocalFunction(inv, method))
                    .ToList();

                branches.Add(new BranchInfo
                {
                    Condition = $"case {TruncateCondition(labelText)}",
                    Invocations = invocations
                });
            }
        }

        return branches;
    }

    private void CollectIfBranches(IfStatementSyntax ifStmt, List<BranchInfo> branches)
    {
        var condition = ifStmt.Condition.ToString();
        var method = ifStmt.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();

        var ifInvocations = ifStmt.Statement.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(inv => method == null || !IsInsideNestedLambdaOrLocalFunction(inv, method))
            .ToList();

        branches.Add(new BranchInfo
        {
            Condition = $"if ({TruncateCondition(condition)})",
            Invocations = ifInvocations
        });

        if (ifStmt.Else != null)
        {
            var elseStmt = ifStmt.Else.Statement;

            if (elseStmt is IfStatementSyntax elseIfStmt)
            {
                CollectIfBranches(elseIfStmt, branches);
            }
            else
            {
                var elseInvocations = elseStmt.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Where(inv => method == null || !IsInsideNestedLambdaOrLocalFunction(inv, method))
                    .ToList();

                branches.Add(new BranchInfo
                {
                    Condition = $"else ({TruncateCondition(condition)})",
                    Invocations = elseInvocations
                });
            }
        }
    }

    private (string className, string methodName)? ResolveMethodTarget(InvocationExpressionSyntax invocation)
    {
        var expr = invocation.Expression;

        if (expr is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;
            var targetExpr = memberAccess.Expression.ToString();
            return (targetExpr, methodName);
        }

        if (expr is IdentifierNameSyntax identifier)
        {
            var methodName = identifier.Identifier.Text;
            var symbol = _context.SemanticModel?.GetSymbolInfo(invocation).Symbol;
            if (symbol is IMethodSymbol methodSymbol)
            {
                var className = methodSymbol.ContainingType?.Name ?? "?";
                return (className, methodName);
            }
            var containingClass = invocation.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            var className2 = containingClass?.Identifier.Text ?? "?";
            return (className2, methodName);
        }

        return null;
    }

    private CallChain CreateChainFromPath(List<CallChainNode> path)
    {
        _chainCounter++;
        return new CallChain
        {
            ChainId = _chainCounter,
            EntryMethod = path.FirstOrDefault()?.MethodFullName ?? "",
            Nodes = new List<CallChainNode>(path)
        };
    }

    private static string GetContainingClassName(SyntaxNode node)
    {
        return node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "Global";
    }

    private static bool IsPublicOrInternal(MethodDeclarationSyntax method)
    {
        return method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword));
    }

    private static string TruncateCondition(string condition, int maxLen = 40)
    {
        return condition.Length > maxLen ? condition[..maxLen] + "..." : condition;
    }

    private class BranchInfo
    {
        public string Condition { get; set; } = "";
        public List<InvocationExpressionSyntax> Invocations { get; set; } = new();
    }
}
