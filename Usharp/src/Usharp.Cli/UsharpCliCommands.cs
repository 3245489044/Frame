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
          obs    <file|dir>                                         观测取数（[Obs] 闸A + manifest；未装配后端 → US9002）
          relation-plan <file|dir>                                  关系计划发射（operations / invariants / derivations）
          verify <file|dir> [--emit-flows]                         框架一致性审计（码表/严重度/重复 id/前缀表；--emit-flows 追加发射期诊断）
          verify --suite <dir>                                     回归套件（按 expected.tsv 逐文件断言 code/severity/line/exit）
        """;

    /// <summary>命令入口。</summary>
    public static int Run(
        IReadOnlyList<string> args, TextWriter output,
        IUsharpGraphBackend? backend = null, IUsharpObservabilityBackend? obsBackend = null)
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
            "obs" => RunWithPath(rest, output, (path, writer) => Obs(path, writer, obsBackend)),
            "relation-plan" => RunWithPath(rest, output, RelationPlan),
            "verify" => RunVerify(rest, output),
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

        return Finish(compilation, errors);
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

        return Finish(compilation, errors);
    }

    /// <summary>矩阵装配取数：绑定（值域矩阵 / 转移表）+ 装配诊断。</summary>
    public static int Matrix(string path, TextWriter output)
    {
        var compilation = Compile(path, output, out var failure);
        if (compilation is null) return failure;

        var result = UsharpMatrixEmitter.Emit(compilation);
        output.WriteLine($"矩阵绑定 {result.Bindings.Count} 个 / 装配诊断 {result.Diagnostics.Count}");
        foreach (var b in result.Bindings)
            output.WriteLine($"   · {b.MatrixId}（{KindLabel(b.Kind)}"
                             + $"，over={b.OverSpaceId}，行 {b.RowCount}）");
        WriteDiagnostics(result.Diagnostics, output);

        return Finish(compilation, result.HasErrors ? 1 : 0);
    }

    /// <summary>矩阵读法标签（<c>matrix</c> 取数可读性；读法集合权威 = <see cref="UsharpMatrixKind"/>）。</summary>
    private static string KindLabel(UsharpMatrixKind kind) => kind switch
    {
        UsharpMatrixKind.Parameter => "值域矩阵",
        UsharpMatrixKind.Decision => "判据矩阵",
        _ => "转移矩阵",
    };

    /// <summary>
    /// 观测取数（批 O5）：<c>[Obs]</c> 闸A 诊断 + 契约登记 + manifest 产出。
    /// 未装配观测后端（默认）→ 观测面产出跳过并可见化 <see cref="UsharpCodes.ObsBackendUnavailable"/>（不假装产出）。
    /// </summary>
    public static int Obs(string path, TextWriter output, IUsharpObservabilityBackend? backend = null)
    {
        var compilation = Compile(path, output, out var failure);
        if (compilation is null) return failure;

        output.WriteLine($"[Obs] 作用域 {compilation.Obs.Scopes.Count} / 实体 {compilation.Obs.Entities.Count} / "
                         + $"事件 {compilation.Obs.Events.Count} / version={(compilation.Obs.FileVersion ?? "(未声明)")}");

        var service = new UsharpObsService(backend);
        foreach (var notice in service.Analyze(compilation))
            output.WriteLine($"   · {notice.Code}: {notice.Message}");

        output.WriteLine(service.RenderManifest(compilation));
        return compilation.HasErrors ? ExitFailed : ExitOk;
    }

    /// <summary>
    /// 框架一致性审计（R-4/R-12 护栏）：码表唯一性 / 严重度与码表一致 / 重复 id / 前缀表一致；
    /// <c>emitFlows</c> 为真时追加全部 <c>[Flow]</c> 的发射期诊断（US0018/US5002 等可回归）。
    /// </summary>
    /// <param name="path">源文件或目录。</param>
    /// <param name="output">输出。</param>
    /// <param name="emitFlows">是否追加发射期诊断。</param>
    public static int Verify(string path, TextWriter output, bool emitFlows = false)
    {
        var compilation = Compile(path, output, out var failure);
        if (compilation is null) return failure;

        var s = compilation.Snapshot();
        output.WriteLine($"verify：空间 {s.Spaces} / 矩阵 {s.Matrices} / 流 {s.Flows} / 错误 {s.Errors} / 警告 {s.Warnings}");

        var issues = UsharpFrameworkAudit.Run(compilation);
        output.WriteLine($"框架审计：{(issues.Count == 0 ? "通过" : issues.Count + " 项")}");
        foreach (var issue in issues) output.WriteLine($"   · {issue}");

        WriteDiagnostics(compilation.Diagnostics, output);

        var emitErrors = 0;
        if (emitFlows)
        {
            var emitDiagnostics = EmitFlowDiagnostics(compilation);
            output.WriteLine($"发射审计：流 {compilation.Flows.Count} / 诊断 {emitDiagnostics.Count}");
            WriteDiagnostics(emitDiagnostics, output);
            emitErrors = emitDiagnostics.Count(d => d.Severity == UsharpSeverity.Error);
        }

        return compilation.HasErrors || issues.Count > 0 || emitErrors > 0 ? ExitFailed : ExitOk;
    }

    /// <summary>verify 参数解析：<c>[--emit-flows] &lt;path&gt;</c> 或 <c>--suite &lt;dir&gt;</c>。</summary>
    private static int RunVerify(IReadOnlyList<string> args, TextWriter output)
    {
        string? path = null;
        var suite = false;
        var emitFlows = false;

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--suite":
                    suite = true;
                    break;
                case "--emit-flows":
                    emitFlows = true;
                    break;
                default:
                    if (path is not null)
                    {
                        output.WriteLine($"用法错误：verify 只接受一个 <file|dir>（多余参数 '{arg}'）。");
                        output.WriteLine(Usage);
                        return ExitUsage;
                    }
                    path = arg;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine("用法错误：verify 需要 <file|dir> 或 --suite <dir>。");
            output.WriteLine(Usage);
            return ExitUsage;
        }

        return suite ? VerifySuite(path, output) : Verify(path, output, emitFlows);
    }

    /// <summary>
    /// 回归套件：按 <c>&lt;dir&gt;/expected.tsv</c> 逐文件独立编译，断言
    /// <c>(code, severity, line, exit)</c>；可选第 6 列 <c>emit</c> 追加发射期诊断（E-18）。
    /// <para>行格式：<c>file|code|severity|line|exit[|emit]</c>；<c>#</c> 注释；line 支持 <c>*</c>。</para>
    /// </summary>
    public static int VerifySuite(string directory, TextWriter output)
    {
        var manifest = Path.Combine(directory, "expected.tsv");
        if (!File.Exists(manifest))
        {
            output.WriteLine($"回归套件：缺少清单 {manifest}（行格式 file|code|severity|line|exit[|emit]）");
            return ExitUsage;
        }

        var failures = 0;
        var cases = 0;

        foreach (var raw in File.ReadAllLines(manifest))
        {
            var text = raw.Trim();
            if (text.Length == 0 || text.StartsWith('#')) continue;

            var parts = text.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 5)
            {
                output.WriteLine($"   ✗ 用例格式非法：{text}");
                failures++;
                continue;
            }

            cases++;
            var file = Path.Combine(directory, parts[0]);
            if (!File.Exists(file))
            {
                output.WriteLine($"   ✗ {parts[0]}: 文件不存在");
                failures++;
                continue;
            }

            var compilation = UsharpCompilation.CompileSources(UsharpSourceLoader.FromPath(file));
            var diagnostics = compilation.Diagnostics.ToList();
            var expectEmit = parts.Length >= 6 && string.Equals(parts[5], "emit", StringComparison.OrdinalIgnoreCase);
            if (expectEmit) diagnostics.AddRange(EmitFlowDiagnostics(compilation));

            var code = parts[1];
            var severity = parts[2];
            var line = parts[3];
            var expectedExit = int.TryParse(parts[4], out var parsedExit) ? parsedExit : ExitFailed;

            var matched = diagnostics.Any(d =>
                string.Equals(d.Code, code, StringComparison.Ordinal)
                && string.Equals(SeverityText(d.Severity), severity, StringComparison.OrdinalIgnoreCase)
                && (line == "*" || d.LineNumber.ToString() == line));

            var actualExit = compilation.HasErrors || diagnostics.Any(d => d.Severity == UsharpSeverity.Error)
                ? ExitFailed
                : ExitOk;

            if (!matched)
            {
                failures++;
                output.WriteLine($"   ✗ {parts[0]}: 未命中 {code}/{severity}@{line}（实得：{Describe(diagnostics)}）");
            }

            if (actualExit != expectedExit)
            {
                failures++;
                output.WriteLine($"   ✗ {parts[0]}: 退出码期望 {expectedExit}，实得 {actualExit}");
            }
        }

        output.WriteLine($"回归套件：{cases} 例 / {(failures == 0 ? "通过" : failures + " 项失败")}");
        return failures == 0 ? ExitOk : ExitFailed;
    }

    /// <summary>全部 <c>[Flow]</c> 的发射期诊断（去重；E-18/E-4 回归用）。</summary>
    private static IReadOnlyList<UsharpDiagnostic> EmitFlowDiagnostics(UsharpCompilation compilation)
    {
        var bag = new UsharpDiagnosticBag();
        foreach (var flow in compilation.Flows)
        {
            var result = Usharp.Emit.UsharpRoundSpecEmitter.Emit(compilation, flow.FlowId);
            bag.AddRange(result.Diagnostics);
        }

        bag.AddRange(Usharp.Emit.UsharpDeclarationLoaderSelfCheck.Run()
            .Select(issue => UsharpDiagnostic.Error(UsharpCodes.DeclarationLoadFailure, issue, "(selfcheck)", 0)));
        return bag.Items;
    }

    private static string SeverityText(UsharpSeverity severity) => severity switch
    {
        UsharpSeverity.Error => "error",
        UsharpSeverity.Warning => "warn",
        _ => "info",
    };

    private static string Describe(IReadOnlyList<UsharpDiagnostic> diagnostics)
        => diagnostics.Count == 0
            ? "(无诊断)"
            : string.Join(", ", diagnostics.Select(d => $"{d.Code}/{SeverityText(d.Severity)}@{d.LineNumber}"));

    // ---------- 内部：解析与报表 ----------

    /// <summary>统一收尾：编译期 error + 本节错误（E-2；禁止只看本节）。</summary>
    private static int Finish(UsharpCompilation compilation, int sectionErrors)
        => compilation.HasErrors || sectionErrors > 0 ? ExitFailed : ExitOk;

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

    /// <summary>
    /// 关系计划发射：<c>[Relation]/[Invariant]/[Derive]</c> → <c>relation.plan.generated.json</c>（stdout）。
    /// <para>无声明 ⇒ <c>US8122</c>（不产出空计划）；编译有 error ⇒ 只打印诊断，不产出计划。</para>
    /// </summary>
    public static int RelationPlan(string path, TextWriter output)
    {
        var compilation = Compile(path, output, out var failure);
        if (compilation is null) return failure;

        WriteDiagnostics(compilation.Diagnostics, output);
        if (compilation.HasErrors) return ExitFailed;

        var result = Usharp.Emit.UsharpRelationPlanEmitter.Emit(compilation);
        WriteDiagnostics(result.Diagnostics, output);
        if (result.Json is null) return ExitFailed;

        output.WriteLine(result.Json);
        return ExitOk;
    }

    private static UsharpCompilation? Compile(string path, TextWriter output, out int exitCode)
    {
        exitCode = ExitOk;
        if (string.IsNullOrWhiteSpace(path) || !UsharpSourceLoader.Exists(path))
        {
            output.WriteLine($"路径不存在：{path}");
            exitCode = ExitUsage;
            return null;
        }
        return UsharpCompilation.CompileSources(UsharpSourceLoader.FromPath(path));
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
            output.WriteLine(UsharpDiagnosticRenderer.Empty);
            return;
        }
        foreach (var line in UsharpDiagnosticRenderer.Lines(diagnostics)) output.WriteLine(line);
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

        foreach (var decl in compilation.Declarations.Nodes)
        {
            if (ResourceNodeType.From(decl) is { Deprecated: true } node && stepKeys.Contains(node.Name))
            {
                issues.Add(UsharpDiagnostic.Warn(UsharpCodes.DeprecatedReferenced,
                    $"[Node] {node.Name} 已废弃（replaced-by={(string.IsNullOrWhiteSpace(node.ReplacedBy) ? "(未声明)" : node.ReplacedBy)}），"
                    + "仍被流步骤引用 —— 请迁移到替代节点",
                    node.SourceFile, node.LineNumber));
            }
        }

        foreach (var decl in compilation.Declarations.Edges)
        {
            if (ReferenceEdgeType.From(decl) is { Deprecated: true } edge && edgeTypes.Contains(edge.Name))
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
