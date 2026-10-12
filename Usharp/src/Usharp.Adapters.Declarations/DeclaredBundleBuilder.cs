using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Operation.HierarchyMethod.Nodes.Relations;
using Usharp.Adapters.UniversalReference;
using Usharp.Emit;
using Usharp.Semantics;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// 冻结声明产物（DeclaredBundle）的**构建结果**：manifest / round-spec / reference-schema 三份 JSON
/// 文本 + 源指纹 + 编译错误计数。纯数据，不承载读取/写入策略。
/// </summary>
/// <param name="ManifestJson">顶层清单 JSON（flows/nodes/functions/ports/fingerprint）。</param>
/// <param name="RoundSpecJson">逐 <c>[Flow]</c> 的骨架链 JSON（flowId → chain）。</param>
/// <param name="ReferenceSchemaJson">引用词表 Schema JSON（与 <c>reference-schema.generated.json</c> 同形）。</param>
/// <param name="SourceFingerprint">源指纹（全部声明文档内容 SHA256；用于新鲜度对账）。</param>
/// <param name="Errors">编译 error 数（闸A 判据；&gt;0 ⇒ 产物不可信）。</param>
/// <param name="Files">参与本次编译的声明文件（路径序）。</param>
public sealed record DeclaredBundleArtifacts(
    string ManifestJson,
    string RoundSpecJson,
    string ReferenceSchemaJson,
    string SourceFingerprint,
    int Errors,
    IReadOnlyList<string> Files);

/// <summary>
/// <b>声明单点编译器</b>（G1）：对声明面（<c>Config\nodes\*.ushar</c> + <c>Config\workflows\*.ushar</c>）执行
/// <b>唯一一次</b> <see cref="UsharpCompilation.Compile"/>，产出冻结产物（manifest / round-spec / reference-schema）。
/// <para>
/// 收敛目标（方案 §4.1）：消除「同一份 <c>.ushar</c> 被 ≥5 条路径各自编译」与「失败姿态五路分叉」——
/// 编译只在此处发生一次，其余消费方一律读产物（见 <see cref="DeclaredBundle"/>）。
/// </para>
/// <para>本类零领域逻辑：只把既有发射器（<see cref="UsharpRoundSpecEmitter"/> / <see cref="UsharpReferenceSchemaEmitter"/>）
/// 的产物汇聚为一份快照，不重建解析/校验。</para>
/// </summary>
public static class DeclaredBundleBuilder
{
    /// <summary>节点声明子目录名。</summary>
    public const string NodesDirName = "nodes";

    /// <summary>工作流声明子目录名。</summary>
    public const string WorkflowsDirName = "workflows";

    /// <summary>产物文件名（磁盘形态）。</summary>
    public const string ManifestFileName = "declared-manifest.json";

    /// <summary>round-spec 产物文件名。</summary>
    public const string RoundSpecFileName = "round-spec.generated.json";

    /// <summary>reference-schema 产物文件名。</summary>
    public const string ReferenceSchemaFileName = "reference-schema.generated.json";

    /// <summary>manifest 顶层 schema 标识（哨兵，防手改/漂移）。</summary>
    public const string ManifestSchema = "declared-manifest-v1";

    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>从配置根构建（<c>Config\nodes</c> + <c>Config\workflows</c>；二目录缺失即空集，不兜底内容）。</summary>
    /// <param name="configRoot">配置根目录（含 <c>nodes</c> / <c>workflows</c> 子目录）。</param>
    public static DeclaredBundleArtifacts Build(string configRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configRoot);
        return Build(LoadConfigDocuments(configRoot));
    }

    /// <summary>装载配置根下声明文档（<c>nodes</c> 目录全部 + <c>workflows</c> 目录全部，路径序）。</summary>
    /// <param name="configRoot">配置根目录。</param>
    public static IReadOnlyList<(string Path, string Text)> LoadConfigDocuments(string configRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configRoot);
        var documents = new List<(string Path, string Text)>();
        AppendDirectory(documents, Path.Combine(configRoot, NodesDirName));
        AppendDirectory(documents, Path.Combine(configRoot, WorkflowsDirName));
        return documents;
    }

    /// <summary>由给定声明文档构建（<b>唯一一次</b>编译；调用方负责文档集，编译序 = 传入序）。</summary>
    /// <param name="documents">声明文档（路径 + 文本）。</param>
    public static DeclaredBundleArtifacts Build(IReadOnlyList<(string Path, string Text)> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var compilation = UsharpCompilation.Compile(documents);
        var fingerprint = Fingerprint(documents);
        var nodes = UsharpNodePortReader.Read(compilation.Declarations);
        var manifest = BuildManifest(compilation, nodes, fingerprint);
        var roundSpec = BuildRoundSpec(compilation);
        var referenceSchema = UsharpReferenceSchemaEmitter.ToJson(UsharpReferenceSchemaEmitter.Emit(compilation));

        return new DeclaredBundleArtifacts(
            JsonSerializer.Serialize(manifest, ManifestOptions),
            JsonSerializer.Serialize(roundSpec, ManifestOptions),
            referenceSchema,
            fingerprint,
            compilation.Errors.Count,
            documents.Select(d => d.Path).ToList());
    }

    /// <summary>写出三份产物到目录（存在即覆盖；产物名 = 各 <c>FileName</c> 常量）。</summary>
    /// <param name="outputDir">输出目录（不存在则创建）。</param>
    /// <param name="artifacts">构建结果。</param>
    public static void WriteTo(string outputDir, DeclaredBundleArtifacts artifacts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);
        ArgumentNullException.ThrowIfNull(artifacts);

        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, ManifestFileName), artifacts.ManifestJson, Encoding.UTF8);
        File.WriteAllText(Path.Combine(outputDir, RoundSpecFileName), artifacts.RoundSpecJson, Encoding.UTF8);
        File.WriteAllText(Path.Combine(outputDir, ReferenceSchemaFileName), artifacts.ReferenceSchemaJson, Encoding.UTF8);
    }

    /// <summary>源指纹：全部文档（路径 + 文本）按路径序拼接后的 SHA256（十六进制小写）。</summary>
    /// <param name="documents">声明文档。</param>
    public static string Fingerprint(IEnumerable<(string Path, string Text)> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var buffer = new StringBuilder();
        foreach (var d in documents.OrderBy(d => d.Path, StringComparer.Ordinal))
        {
            buffer.Append(d.Path).Append('|').Append(d.Text ?? string.Empty).Append('\n');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buffer.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ManifestDocument BuildManifest(
        UsharpCompilation compilation, IReadOnlyDictionary<string, UsharpNodeSpec> nodes, string fingerprint)
    {
        var flows = new List<ManifestFlow>(compilation.Flows.Count);
        foreach (var flow in compilation.Flows)
        {
            if (string.IsNullOrWhiteSpace(flow.FlowId)) continue;

            var steps = new List<ManifestStep>(flow.Steps.Count);
            // 同文件节点表（D3）：步骤解析优先取本流所在文件的 [Node]，与 UsharpRoundSpecEmitter 同源口径。
            var sameFileNodes = UsharpNodePortReader.Read(compilation.Declarations, flow.SourceFile);
            foreach (var step in flow.Steps)
            {
                if (string.IsNullOrWhiteSpace(step.Key)) continue;
                var spec = sameFileNodes.TryGetValue(step.Key, out var own)
                    ? own
                    : (nodes.TryGetValue(step.Key, out var shared) ? shared : null);
                steps.Add(new ManifestStep(
                    step.Key,
                    spec?.FuncName ?? step.Key,
                    spec?.InputPorts ?? Array.Empty<string>(),
                    spec?.OutputPorts ?? Array.Empty<string>()));
            }

            flows.Add(new ManifestFlow(
                FileName(flow.SourceFile), flow.FlowId, flow.OverSpaceId, flow.SuccessCriterion, steps));
        }

        var nodeManifests = new List<ManifestNode>(nodes.Count);
        foreach (var spec in nodes.Values)
        {
            nodeManifests.Add(new ManifestNode(
                FileName(spec.SourceFile), spec.Name, spec.FuncName, spec.Kind,
                spec.InputPorts, spec.OutputPorts, spec.LineNumber));
        }

        var functions = nodes.Values
            .Select(n => n.FuncName)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        return new ManifestDocument(
            ManifestSchema,
            fingerprint,
            flows,
            nodeManifests,
            functions,
            new ManifestPorts(
                Distinct(nodes.Values.SelectMany(n => n.InputPorts)),
                Distinct(nodes.Values.SelectMany(n => n.OutputPorts))),
            BuildVocabulary(compilation));
    }

    /// <summary>词表（G4）：<c>[Space]</c> 声明 → 空间号 + 种类 + 坐标名表（键型/词元型空间即词表真源）。
    /// <para>坐标**保声明序**（位置即语义：消费方按声明序定位词表取值，禁排序）。</para></summary>
    private static IReadOnlyList<ManifestVocabulary> BuildVocabulary(UsharpCompilation compilation)
    {
        var vocabulary = new List<ManifestVocabulary>(compilation.Spaces.SpaceCount);
        foreach (var space in compilation.Spaces.Spaces)
        {
            vocabulary.Add(new ManifestVocabulary(
                space.SpaceId,
                space.Kind.ToString().ToLowerInvariant(),
                space.Axes.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()));
        }
        return vocabulary;
    }

    private static IReadOnlyDictionary<string, string> BuildRoundSpec(UsharpCompilation compilation)
    {
        var spec = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var flow in compilation.Flows)
        {
            if (string.IsNullOrWhiteSpace(flow.FlowId)) continue;
            var emitted = UsharpRoundSpecEmitter.Emit(compilation, flow.FlowId);
            if (emitted.Spec is null || emitted.HasErrors || emitted.Spec.Steps.Count == 0) continue;
            spec[flow.FlowId] = RoundSpecChainMapper.ToJson(emitted.Spec);
        }
        return spec;
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string> values)
        => values.Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

    private static string FileName(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileName(path!);

    private static void AppendDirectory(List<(string Path, string Text)> sink, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*" + UsharpSourceLoader.Extension, SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.Ordinal))
            sink.Add((file, File.ReadAllText(file)));
    }

    // ---- manifest 结构（camelCase 序列化；键名与方案 §4.1 一致）----
    private sealed record ManifestDocument(
        string Schema, string Fingerprint, IReadOnlyList<ManifestFlow> Flows,
        IReadOnlyList<ManifestNode> Nodes, IReadOnlyList<string> Functions, ManifestPorts Ports,
        IReadOnlyList<ManifestVocabulary> Vocabulary);

    private sealed record ManifestVocabulary(string Space, string Kind, IReadOnlyList<string> Keys);

    private sealed record ManifestFlow(
        string File, string FlowId, string? Over, string? Success, IReadOnlyList<ManifestStep> Steps);

    private sealed record ManifestStep(
        string Key, string Fn, IReadOnlyList<string> Inports, IReadOnlyList<string> Outports);

    private sealed record ManifestNode(
        string File, string Name, string Fn, string? Kind,
        IReadOnlyList<string> Inports, IReadOnlyList<string> Outports, int Line);

    private sealed record ManifestPorts(IReadOnlyList<string> Inports, IReadOnlyList<string> Outports);
}
