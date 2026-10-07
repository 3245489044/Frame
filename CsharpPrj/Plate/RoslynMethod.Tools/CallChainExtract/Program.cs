using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace RoslynMethod.Tools.CallChainExtract;

internal static class Program
{
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", "_deprecated", "backups", "generated", "node_modules", ".venv"
    };

    private static readonly MetadataReference[] StandardReferences =
    {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Stack<>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ValueTuple).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
        MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Linq").Location)
    };

    /// <summary>单个调用点信息。Target 为语义解析后的类型限定签名（项目内方法可解析，否则为空）。</summary>
    private sealed class CallInfo
    {
        public string Name { get; init; } = "";
        public string Target { get; init; } = "";
        public int Line { get; init; }
    }

    /// <summary>单个方法及其调用信息。</summary>
    private sealed class MethodInfo
    {
        public string File { get; init; } = "";
        public string Type { get; init; } = "";
        public string Method { get; init; } = "";
        public string Signature { get; init; } = "";
        public int Line { get; init; }
        public List<CallInfo> Calls { get; init; } = new();
    }

    private static int Main(string[] args)
    {
        string root = ".";
        bool jsonOutput = false;
        bool analyze = false;
        string? methodFilter = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "-path":
                case "--path":
                    if (i + 1 < args.Length)
                    {
                        root = args[++i];
                    }

                    break;
                case "-json":
                case "--json":
                    jsonOutput = true;
                    break;
                case "-analyze":
                case "--analyze":
                    analyze = true;
                    break;
                case "-method":
                case "--method":
                    if (i + 1 < args.Length)
                    {
                        methodFilter = args[++i];
                    }

                    break;
                case "-h":
                case "--help":
                    PrintUsage();
                    return 0;
            }
        }

        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"[ERROR] 目录不存在: {root}");
            return 2;
        }

        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 阶段一：解析全部文件为语法树（语义绑定需要整工程一次性编译）
        var trees = new List<(string Path, SyntaxTree Tree)>();
        int total = 0;
        int skipped = 0;
        foreach (var file in files)
        {
            total++;
            var tree = ParseFile(file);
            if (tree is null)
            {
                skipped++;
                continue;
            }

            trees.Add((file, tree));
        }

        // 阶段二：整工程编译，获得跨文件的语义解析能力
        var compilation = CSharpCompilation.Create(
            "CallChainAnalysis",
            trees.Select(t => t.Tree),
            StandardReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Debug)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        // 阶段三：逐文件提取方法及其语义调用边
        var methods = new List<MethodInfo>();
        foreach (var (path, tree) in trees)
        {
            ExtractMethods(path, tree, compilation, methodFilter, methods);
        }

        if (analyze)
        {
            PrintAnalyze(methods);
            return 0;
        }

        if (jsonOutput)
        {
            var payload = new
            {
                root = Path.GetFullPath(root),
                totalFiles = total,
                parsedFiles = trees.Count,
                skippedFiles = skipped,
                methods
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
        else
        {
            foreach (var m in methods)
            {
                Console.WriteLine($"{m.File} -> {m.Signature} (行 {m.Line})");
                foreach (var call in m.Calls)
                {
                    string target = string.IsNullOrEmpty(call.Target) ? call.Name : $"{call.Name} → {call.Target}";
                    Console.WriteLine($"    调用 {target} (行 {call.Line})");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"总文件数: {total}");
            Console.WriteLine($"成功解析: {trees.Count}");
            Console.WriteLine($"跳过(语法错误): {skipped}");
        }

        return 0;
    }

    /// <summary>读取并解析单个文件；存在 Error 级语法诊断时返回 null。</summary>
    private static SyntaxTree? ParseFile(string path)
    {
        string text;
        try
        {
            (_, text) = ReadFileWithEncoding(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] 读取失败, 跳过: {path}: {ex.Message}");
            return null;
        }

        var tree = CSharpSourceProvider.Instance.Parse(text, path);
        if (tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return null;
        }

        return tree;
    }

    /// <summary>
    /// 提取单个文件内所有方法及其调用链。调用边经语义模型解析为类型限定签名
    /// （仅本编译内定义的方法可解析为 Target；外部/BCL 调用保持原始名）。
    /// </summary>
    private static void ExtractMethods(
        string path,
        SyntaxTree tree,
        Compilation compilation,
        string? methodFilter,
        List<MethodInfo> sink)
    {
        var root = tree.GetRoot();
        var model = compilation.GetSemanticModel(tree);
        var assembly = compilation.Assembly;

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            string methodName = method.Identifier.ValueText;
            if (!string.IsNullOrEmpty(methodFilter)
                && !methodName.Contains(methodFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string typeName = GetTypeName(method);
            string signature = $"{typeName}.{methodName}";

            var calls = new List<CallInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string? invokedName = GetInvokedName(invocation);
                if (invokedName is null)
                {
                    continue;
                }

                int callLine = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                string target = ResolveTarget(model, invocation, assembly);
                string dedupeKey = string.IsNullOrEmpty(target) ? invokedName : target;
                // 同方法内对同一被调点只记首次（以语义签名优先去重）
                if (seen.Add(dedupeKey))
                {
                    calls.Add(new CallInfo { Name = invokedName, Target = target, Line = callLine });
                }
            }

            int defLine = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            sink.Add(new MethodInfo
            {
                File = path,
                Type = typeName,
                Method = methodName,
                Signature = signature,
                Line = defLine,
                Calls = calls
            });
        }
    }

    /// <summary>
    /// 语义解析调用点：返回本编译内定义的方法的类型限定签名（Type.Method），
    /// 否则返回空串（外部/BCL/无法解析）。
    /// </summary>
    private static string ResolveTarget(SemanticModel model, InvocationExpressionSyntax invocation, IAssemblySymbol assembly)
    {
        var symbol = model.GetSymbolInfo(invocation).Symbol;
        if (symbol is IMethodSymbol method
            && SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, assembly))
        {
            return $"{method.ContainingType.ToDisplayString()}.{method.Name}";
        }

        // 重载/泛型歧义时回退候选符号
        var candidate = model.GetSymbolInfo(invocation).CandidateSymbols
            .OfType<IMethodSymbol>()
            .FirstOrDefault(c => SymbolEqualityComparer.Default.Equals(c.ContainingAssembly, assembly));
        return candidate is null
            ? ""
            : $"{candidate.ContainingType.ToDisplayString()}.{candidate.Name}";
    }

    /// <summary>以签名级调用图输出环数/最长链/扇出等 R4 判据汇总。</summary>
    private static void PrintAnalyze(List<MethodInfo> methods)
    {
        var signatures = methods.Select(m => m.Signature).ToHashSet(StringComparer.Ordinal);

        // 签名级调用图：本编译内方法的精确调用边（同名合并噪声已被语义解析消除）
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var m in methods)
        {
            if (!graph.ContainsKey(m.Signature))
            {
                graph[m.Signature] = new HashSet<string>(StringComparer.Ordinal);
            }

            foreach (var c in m.Calls)
            {
                if (!string.IsNullOrEmpty(c.Target) && signatures.Contains(c.Target))
                {
                    graph[m.Signature].Add(c.Target);
                }
            }
        }

        // Kahn 拓扑：环数 = 未完成节点数
        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var k in graph.Keys)
        {
            indegree[k] = 0;
        }

        foreach (var k in graph.Keys)
        {
            foreach (var t in graph[k])
            {
                if (indegree.ContainsKey(t))
                {
                    indegree[t]++;
                }
            }
        }

        var queue = new Queue<string>();
        foreach (var k in graph.Keys)
        {
            if (indegree[k] == 0)
            {
                queue.Enqueue(k);
            }
        }

        var order = new List<string>();
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            order.Add(u);
            foreach (var t in graph[u])
            {
                indegree[t]--;
                if (indegree[t] == 0)
                {
                    queue.Enqueue(t);
                }
            }
        }

        int nodeCount = graph.Count;
        int unprocessed = nodeCount - order.Count; // Kahn 未完成节点（含环成员及其依赖者）

        // Tarjan SCC：真环数（SCC 大小 > 1）与最大环
        var scc = FindStronglyConnectedComponents(graph);
        int realCycles = scc.Count(c => c.Count > 1);
        int largestCycle = scc.Count == 0 ? 0 : scc.Max(c => c.Count);
        int selfCycles = scc.Count(c => c.Count == 1 && graph[c[0]].Contains(c[0]));

        // 拓扑序倒推最长调用链
        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var k in graph.Keys)
        {
            depth[k] = 1;
        }

        for (int i = order.Count - 1; i >= 0; i--)
        {
            var u = order[i];
            foreach (var t in graph[u])
            {
                if (depth[t] + 1 > depth[u])
                {
                    depth[u] = depth[t] + 1;
                }
            }
        }

        int maxDepth = depth.Count == 0 ? 0 : depth.Values.Max();
        int deep5 = depth.Values.Count(v => v >= 5);
        int deep8 = depth.Values.Count(v => v >= 8);

        // 扇出 Top（项目内调用边数）
        var fanoutTop = graph
            .OrderByDescending(kv => kv.Value.Count)
            .Take(8)
            .Select(kv => $"{kv.Key}({kv.Value.Count})");

        Console.WriteLine($"方法总数: {methods.Count}  类型限定签名数: {signatures.Count}");
        Console.WriteLine($"签名级调用图: 节点 {nodeCount}  项目内边 {graph.Values.Sum(s => s.Count)}");
        Console.WriteLine($"Kahn 未完成节点: {unprocessed}/{nodeCount}  |  真环(SCC>1): {realCycles}  最大环大小: {largestCycle}  自环(单方法递归): {selfCycles}");
        Console.WriteLine($"最长调用链深度: {maxDepth}  (深度>=5: {deep5}  深度>=8: {deep8})");
        Console.WriteLine($"扇出 Top: {string.Join(", ", fanoutTop)}");
    }

    /// <summary>Tarjan 强连通分量。返回全部分量（含大小为 1 的）。</summary>
    private static List<List<string>> FindStronglyConnectedComponents(Dictionary<string, HashSet<string>> graph)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var result = new List<List<string>>();
        int counter = 0;

        foreach (var v in graph.Keys)
        {
            if (!index.ContainsKey(v))
            {
                StrongConnect(v);
            }
        }

        return result;

        void StrongConnect(string v)
        {
            index[v] = counter;
            low[v] = counter;
            counter++;
            stack.Push(v);
            onStack.Add(v);

            foreach (var w in graph[v])
            {
                if (!index.ContainsKey(w))
                {
                    StrongConnect(w);
                    low[v] = Math.Min(low[v], low[w]);
                }
                else if (onStack.Contains(w))
                {
                    low[v] = Math.Min(low[v], index[w]);
                }
            }

            if (low[v] != index[v])
            {
                return;
            }

            var component = new List<string>();
            string member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                component.Add(member);
            } while (member != v);

            result.Add(component);
        }
    }

    /// <summary>取调用表达式被调方法名(形如 Foo() / this.Foo() / x.Bar() / Foo&lt;T&gt;() 统一取 Foo / Bar)。</summary>
    private static string? GetInvokedName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
            _ => null
        };
    }

    /// <summary>沿语法树向上拼出所属类型名, 形如 Namespace.Class.Method 或 Class.Method。</summary>
    private static string GetTypeName(MethodDeclarationSyntax method)
    {
        var names = new Stack<string>();
        foreach (var node in method.Ancestors())
        {
            switch (node)
            {
                case BaseTypeDeclarationSyntax typeDecl:
                    names.Push(typeDecl.Identifier.ValueText);
                    break;
                case NamespaceDeclarationSyntax nsDecl:
                    names.Push(nsDecl.Name.ToString());
                    break;
                case FileScopedNamespaceDeclarationSyntax fileNsDecl:
                    names.Push(fileNsDecl.Name.ToString());
                    break;
            }
        }

        return names.Count == 0 ? "(global)" : string.Join(".", names);
    }

    private static (Encoding Encoding, string Text) ReadFileWithEncoding(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Encoding encoding;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(true);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(false, true);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(true, true);
        }
        else
        {
            encoding = new UTF8Encoding(false);
        }

        return (encoding, encoding.GetString(bytes));
    }

    private static bool IsExcluded(string fullPath)
    {
        string[] segments = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => ExcludedDirs.Contains(s));
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用法: CallChainExtract -path <根目录> [-json] [-analyze] [-method <名称>] [-h]");
        Console.WriteLine("  -path       C# 项目根目录 (默认: 当前目录)");
        Console.WriteLine("  -json       以 JSON 格式输出到 stdout");
        Console.WriteLine("  -analyze    输出 R4 判据汇总(环数/最长链/扇出)，语义解析类型限定签名");
        Console.WriteLine("  -method     可选过滤: 只输出方法名包含该子串的方法");
        Console.WriteLine("  -h          显示帮助");
    }
}
