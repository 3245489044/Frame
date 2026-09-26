using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Diagnostics;
using Modules.Node.Spec;
using Operation.HierarchyMethod.Nodes.Relations;
using Usharp.Emit;
using Usharp.Semantics;

namespace Usharp.Adapters.Node;

/// <summary>
/// <c>.ushar</c> 声明 → 节点框架定义集（<see cref="ISpecSource"/> 的**语言装配**）。
/// <para>
/// 链路：<c>.ushar</c> → <see cref="UsharpCompilation.Compile"/> → 每个 <c>[Flow]</c> 经
/// <see cref="UsharpRoundSpecEmitter.Emit"/> → <see cref="RoundSpecChainMapper.ToJson"/>
/// （**键连接唯一映射点**，禁第二份映射）→ <see cref="SpecDocument"/>（链 JSON）。
/// </para>
/// <para>
/// 纪律：① 编译期**零写库**（只产定义集；物化仍归 <c>NodeMaterializer</c>）；
/// ② 诊断**单向投影**到 <see cref="IDefinitionDiagnostics"/>（禁反向解析）；
/// ③ 语言级 error **不被下游降级**为 warn；不产出即不产出（禁静默补内容）。
/// </para>
/// <para>
/// 耦合方向：本工程 = **唯一引 <c>Modules.Node</c> 的 Usharp 工程**；桥接类型只许出现在此处。
/// </para>
/// </summary>
public sealed class UsharpNodeSpecSource : ISpecSource
{
    private readonly IReadOnlyList<(string Path, string Text)> _documents;
    private readonly IDefinitionDiagnostics? _diagnostics;

    /// <param name="sourceId">来源标识（可辨来源，N9）。</param>
    /// <param name="documents">声明文档（路径 + 文本）。</param>
    /// <param name="diagnostics">定义期诊断出口（单向投影；缺省不落接收端）。</param>
    public UsharpNodeSpecSource(
        string sourceId,
        IReadOnlyList<(string Path, string Text)> documents,
        IDefinitionDiagnostics? diagnostics = null)
    {
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _diagnostics = diagnostics;
    }

    /// <inheritdoc />
    public string SourceId { get; }

    /// <summary>从目录装载全部声明文档（目录缺失/为空 ⇒ 空集 + 可见化，不兜底内容）。</summary>
    /// <param name="sourceId">来源标识。</param>
    /// <param name="directory">声明目录（其下 <c>*.ushar</c>）。</param>
    /// <param name="diagnostics">定义期诊断出口。</param>
    /// <param name="searchPattern">声明文件模式（缺省 <c>*.ushar</c>）。</param>
    public static UsharpNodeSpecSource FromDirectory(
        string sourceId,
        string directory,
        IDefinitionDiagnostics? diagnostics = null,
        string searchPattern = "*.ushar")
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            diagnostics?.Report(
                "spec.ushar-directory-missing",
                NodeDiagnosticSeverity.Warning,
                $"声明目录未命中：{directory ?? "(null)"}（定义集为空，不兜底）",
                sourceId);

            return new UsharpNodeSpecSource(sourceId, Array.Empty<(string, string)>(), diagnostics);
        }

        var documents = Directory
            .EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .ToArray();

        return new UsharpNodeSpecSource(sourceId, documents, diagnostics);
    }

    /// <inheritdoc />
    public Task<DefinitionSet> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_documents.Count == 0)
        {
            _diagnostics?.Report(
                "spec.ushar-empty",
                NodeDiagnosticSeverity.Warning,
                "声明来源无文档：定义集为空（不假装通过）",
                SourceId);

            return Task.FromResult(DefinitionSet.Empty(SourceId));
        }

        var compilation = UsharpCompilation.Compile(_documents.Select(d => (d.Path, d.Text)));
        Project(compilation.Diagnostics);

        // 定义期强校验（§4 校验分工：语言级 error 不被下游降级）：
        // 编译含 error ⇒ **不产出定义集**（诊断原样透出；不静默补内容、不用残缺定义集装配）。
        if (compilation.HasErrors)
        {
            return Task.FromResult(DefinitionSet.Empty(SourceId));
        }

        var chains = new List<SpecDocument>(compilation.Flows.Count);
        foreach (var flow in compilation.Flows)
        {
            var emitted = UsharpRoundSpecEmitter.Emit(compilation, flow.FlowId);
            Project(emitted.Diagnostics);

            // 语言级 error / 空步骤 ⇒ **不产出该链**（定义集不含残缺骨架；诊断已原样透出）。
            if (emitted.Spec is null || emitted.HasErrors || emitted.Spec.Steps.Count == 0)
            {
                continue;
            }

            chains.Add(new SpecDocument(SourceId, flow.FlowId, RoundSpecChainMapper.ToJson(emitted.Spec)));
        }

        return Task.FromResult(new DefinitionSet(
            SourceId,
            chains,
            Array.Empty<SpecDocument>(),
            Array.Empty<SpecDocument>(),
            Array.Empty<SpecDocument>()));
    }

    /// <summary>语言级诊断 → 框架定义期诊断（<b>单向</b>投影：严重级原样，error 不被降级）。</summary>
    private void Project(IReadOnlyList<UsharpDiagnostic> diagnostics)
    {
        if (_diagnostics is null)
        {
            return;
        }

        foreach (var diagnostic in diagnostics)
        {
            _diagnostics.Report(
                diagnostic.Code,
                diagnostic.Severity switch
                {
                    UsharpSeverity.Error => NodeDiagnosticSeverity.Error,
                    UsharpSeverity.Warning => NodeDiagnosticSeverity.Warning,
                    _ => NodeDiagnosticSeverity.Info,
                },
                diagnostic.Message,
                $"{diagnostic.SourceFile}:{diagnostic.LineNumber}");
        }
    }
}
