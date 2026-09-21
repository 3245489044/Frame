using Plate.DslMethod;
using Plate.DslMethod.Schema;
using Usharp.Matrix;
using Usharp.Semantics;

namespace Usharp.Cli;

/// <summary>
/// Usharp 四命令（<c>build</c> / <c>lint</c> / <c>graph</c> / <c>matrix</c>）：只做编排 ——
/// 编译、闸A、引用即边、图能力、矩阵装配一律复用语言核心的单一实现，不另写第二份判据。
/// <para>逻辑与 IO 分离（<see cref="TextWriter"/> 注入），便于测试直接驱动；退出码：
/// <see cref="ExitOk"/> 通过 / <see cref="ExitFailed"/> 有 error / <see cref="ExitUsage"/> 用法错误。</para>
/// </summary>
public static class UsharpCliCommands
{
    public const int ExitOk = 0;
    public const int ExitFailed = 1;
    public const int ExitUsage = 2;

    /// <summary>用法文本（单一真源：--help 与用法错误共用）。</summary>
    public const string Usage = """
        Usharp CLI —— 以矩阵为框架、引用关系为核心的逻辑语言工具链
          build  <file|dir>                                         编译取数（空间/坐标/矩阵/流/引用/边 + 诊断）
          lint   <file|dir>                                         四检：悬空 / 废弃 / 循环 / 反向引用
          graph  <file|dir> [--reachable <n>] [--path <a> <b>]       引用图查询（环检测 / 影响域 / 两点路径）
          matrix <file|dir>                                         矩阵装配取数（值域矩阵 / 转移表）
        """;

    /// <summary>命令入口。</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter output, IUsharpGraphBackend? backend = null)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (args is null || args.Count == 0)
        {
            output.WriteLine(Usage);
            return ExitUsage;
        }

        var command = args[0];
        if (command is "--help" or "-h" or "help")
        {
            output.WriteLine(Usage);
            return ExitOk;
        }

        var rest = args.Skip(1).ToList();
        return command switch
        {
            "build" => RunWithPath(rest, output, Build),
            "matrix" => RunWithPath(rest, output, Matrix),
            "lint" => RunWithPath(rest, output, (path, writer) => Lint(path, writer, backend)),
            "graph" => RunGraph(rest, output, backend),
            _ => Unknown(command, output),
        };
    }

    /// <summary>编译取数：规模快照 + 诊断报表（有 error → 退出码 1）。</summary>
    public static int Build(string path, TextWriter output)
    {
        var compilation = Compile(path, output, out var failure);
        if (compilation is null) return failure;

        var s = compilation.Snapshot();
        output.WriteLine($"空间 {s.Spaces} / 坐标 {s.Coordinates} / 矩阵 {s.Matrices} / 流 {s.Flows} / "
                         + $"引用 {s.Refs} / 边 {s.Edges} / 错误 {s.Errors} / 警告 {s.Warnings}");
        WriteDiagnostics(compilation.Diagnostics, output);

        return compilation.HasErrors ? ExitFailed : ExitOk;
    }

    /// <summary>
    /// 四检：① 悬空引用（<c>[CR]</c> 不可解析，US2001）② 废弃指向（仍被引用的 <c>dep</c> 声明，US2002）
    /// ③ 循环引用（经图后端，US4001；未装配 → US9001 可见化）④ 反向引用（报表，不产生诊断）。
    /// </summary>
    public static int Lint(string path, TextWriter output, IUsharpGraphBackend? backend = null)
    {
        var compilation = Compile(path, output, out var failure);
        if (compilation is null) return failure;

        var errors = 0;

        ReportSection("① 悬空引用", compilation.Diagnostics.Where(d => d.Code == UsharpCodes.CrDangling).ToList(),
            output, ref errors);
        ReportSection("② 废弃指向", DeprecatedReferenced(compilation), output, ref errors);

        var graph = new UsharpGraphService(backend).Analyze(compilation);
        ReportSection("③ 循环引用", graph.Where(d => d.Code == UsharpCodes.CircularReference).ToList(), output, ref errors);
        foreach (var notice in graph.Where(d => d.Code == UsharpCodes.GraphBackendUnavailable))
            output.WriteLine($"   · {notice.Code}: {notice.Message}");

        WriteReverseReferences(compilation, output);

        return errors > 0 ? ExitFailed : ExitOk;
    }

    /// <summary>引用图查询：环检测 / 流完整性 / 影响域（--reachable）/ 两点路径（--path）。</summary>
    public static int Graph(
        string path, TextWriter output, IUsharpGraphBackend? backend = null,
        string? reachable = null, string? from = null, string? to = null)
    {
        var compilation = Compile(path, output, out var failure);
        if (compilation is null) return failure;

        var edges = UsharpEdgeSelection.GraphEdges(compilation);
        output.WriteLine($"引用图：节点 {UsharpEdgeSelection.Nodes(edges).Count} / 边 {edges.Count}"
                         + "（落图子集：不含 Over / RowRef）");

        var service = new UsharpGraphService(backend);
        var diagnostics = service.Analyze(compilation);
        var errors = 0;

        ReportSection("环检测", diagnostics.Where(d => d.Code == UsharpCodes.CircularReference).ToList(), output, ref errors);
        foreach (var notice in diagnostics.Where(d => d.Code == UsharpCodes.GraphBackendUnavailable))
            output.WriteLine($"   · {notice.Code}: {notice.Message}");
        ReportSection("流完整性", diagnostics.Where(d => d.Code == UsharpCodes.FlowIntegrityViolation).ToList(), output, ref errors);

        if (!string.IsNullOrWhiteSpace(reachable))
        {
            var set = service.Reachable(compilation, reachable!);
            output.WriteLine($"影响域（可达集）({reachable})：{set.Count} 个");
            output.WriteLine($"   {string.Join(", ", set.OrderBy(n => n, StringComparer.Ordinal))}");
        }

        if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to))
            output.WriteLine($"两点可达 {from} → {to}：{(service.HasPath(compilation, from!, to!) ? "是" : "否")}");

        return errors > 0 ? ExitFailed : ExitOk;
    }

    /// <summary>矩阵装配取数：绑定（值域矩阵 / 转移表）+ 装配诊断。</summary>
    public static int Matrix(string path, TextWriter output)
    {
        var compilation = Compile(path, output, out var failure);
        if (compilation is null) return failure;

        var result = UsharpMatrixEmitter.Emit(compilation);
        output.WriteLine($"矩阵绑定 {result.Bindings.Count} 个 / 装配诊断 {result.Diagnostics.Count}");
        foreach (var b in result.Bindings)
            output.WriteLine($"   · {b.MatrixId}（{(b.Kind == UsharpMatrixKind.Parameter ? "值域矩阵" : "转移矩阵")}"
                             + $"，over={b.OverSpaceId}，行 {b.RowCount}）");
        WriteDiagnostics(result.Diagnostics, output);

        return result.HasErrors ? ExitFailed : ExitOk;
    }

    // ---------- 内部：解析与报表 ----------

    private static int RunWithPath(IReadOnlyList<string> args, TextWriter output, Func<string, TextWriter, int> action)
    {
        if (args.Count != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            output.WriteLine("用法错误：该命令需要且仅需要一个 <file|dir> 参数。");
            output.WriteLine(Usage);
            return ExitUsage;
        }
        return action(args[0], output);
    }

    private static int RunGraph(IReadOnlyList<string> args, TextWriter output, IUsharpGraphBackend? backend)
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            output.WriteLine("用法错误：graph 需要 <file|dir> 参数。");
            output.WriteLine(Usage);
            return ExitUsage;
        }

        string? reachable = null, from = null, to = null;
        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--reachable" when i + 1 < args.Count:
                    reachable = args[++i];
                    break;
                case "--path" when i + 2 < args.Count:
                    from = args[++i];
                    to = args[++i];
                    break;
                default:
                    output.WriteLine($"用法错误：未知或参数不足的选项 '{args[i]}'。");
                    output.WriteLine(Usage);
                    return ExitUsage;
            }
        }

        return Graph(args[0], output, backend, reachable, from, to);
    }

    private static int Unknown(string command, TextWriter output)
    {
        output.WriteLine($"未知命令：{command}");
        output.WriteLine(Usage);
        return ExitUsage;
    }

    private static UsharpCompilation? Compile(string path, TextWriter output, out int exitCode)
    {
        exitCode = ExitOk;
        if (string.IsNullOrWhiteSpace(path) || !UsharpCliSources.Exists(path))
        {
            output.WriteLine($"路径不存在：{path}");
            exitCode = ExitUsage;
            return null;
        }
        return UsharpCompilation.Compile(UsharpCliSources.Load(path));
    }

    private static void ReportSection(string title, IReadOnlyList<UsharpDiagnostic> items, TextWriter output, ref int errors)
    {
        output.WriteLine($"{title}：{(items.Count == 0 ? "通过" : items.Count + " 项")}");
        foreach (var d in items)
        {
            output.WriteLine($"   {d}");
            if (d.Severity == UsharpSeverity.Error) errors++;
        }
    }

    private static void WriteDiagnostics(IReadOnlyList<UsharpDiagnostic> diagnostics, TextWriter output)
    {
        if (diagnostics.Count == 0)
        {
            output.WriteLine("(no diagnostics)");
            return;
        }
        foreach (var d in diagnostics) output.WriteLine(d.ToString());
    }

    /// <summary>④ 反向引用报表（谁引用了谁；<c>DslIndex.Reverse</c> 单一来源）。</summary>
    private static void WriteReverseReferences(UsharpCompilation compilation, TextWriter output)
    {
        var reverse = compilation.Index.Reverse;
        output.WriteLine($"④ 反向引用：{reverse.Count} 个符号被 [CR] 引用");
        foreach (var entry in reverse.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            output.WriteLine($"   · {entry.Key} ← {string.Join(", ", entry.Value.Select(r => r.Raw))}");
    }

    /// <summary>② 废弃指向：标记 <c>dep</c> 的 <c>[Node]</c>/<c>[Edge]</c> 声明仍被流步骤 / 引用边使用。</summary>
    public static IReadOnlyList<UsharpDiagnostic> DeprecatedReferenced(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var issues = new List<UsharpDiagnostic>();
        var stepKeys = new HashSet<string>(
            compilation.Flows.SelectMany(f => f.Steps).Select(s => s.Key), StringComparer.Ordinal);
        var edgeTypes = new HashSet<string>(
            UsharpEdgeSelection.GraphEdges(compilation).Select(e => e.Type), StringComparer.Ordinal);

        foreach (var doc in compilation.Documents)
            foreach (var decl in doc.Declarations)
            {
                if (decl.Kind == DslSymbolKind.Node
                    && ResourceNodeType.From(decl) is { Deprecated: true } node
                    && stepKeys.Contains(node.Name))
                {
                    issues.Add(UsharpDiagnostic.Warn(UsharpCodes.DeprecatedReferenced,
                        $"[Node] {node.Name} 已废弃（replaced-by={(string.IsNullOrWhiteSpace(node.ReplacedBy) ? "(未声明)" : node.ReplacedBy)}），"
                        + "仍被流步骤引用 —— 请迁移到替代节点",
                        node.SourceFile, node.LineNumber));
                }

                if (decl.Kind == DslSymbolKind.Edge
                    && ReferenceEdgeType.From(decl) is { Deprecated: true } edge
                    && edgeTypes.Contains(edge.Name))
                {
                    issues.Add(UsharpDiagnostic.Warn(UsharpCodes.DeprecatedReferenced,
                        $"[Edge] {edge.Name} 已废弃（replaced-by={(string.IsNullOrWhiteSpace(edge.ReplacedBy) ? "(未声明)" : edge.ReplacedBy)}），"
                        + "仍被引用边使用 —— 请迁移到替代边类型",
                        edge.SourceFile, edge.LineNumber));
                }
            }

        return issues;
    }
}
