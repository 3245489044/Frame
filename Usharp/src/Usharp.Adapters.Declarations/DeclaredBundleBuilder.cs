using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Plate.DslMethod.Schema;
using Usharp.Adapters.UniversalReference;
using Usharp.Emit;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// 冻结声明产物（DeclaredBundle）的**构建结果**：manifest / round-spec / reference-schema 三份 JSON
/// 文本（manifest 另出 gzip 嵌入副本）+ 源指纹 + 编译错误计数。纯数据，不承载读取/写入策略。
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
    string KeySpaceJson,
    string CommandsJson,
    string SourceFingerprint,
    int Errors,
    IReadOnlyList<string> Files,
    IReadOnlyList<UsharpDiagnostic> Diagnostics);

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

    /// <summary>键空间声明子目录名（<c>[KeySpace]</c>）。</summary>
    public const string KeyspacesDirName = "keyspaces";

    /// <summary>连接点命令声明子目录名（<c>[Command]</c>）。</summary>
    public const string CommandsDirName = "commands";

    /// <summary>状态/矩阵声明子目录名（<c>[Space]</c> / <c>[Matrix]</c>；O2 迁出 commands）。</summary>
    public const string StateDirName = "state";

    /// <summary>提示词段声明文件名（配置根下；<c>[Segment]</c>）。</summary>
    public const string SegmentsFileName = "prompt-segments.ushar";

    /// <summary>键空间产物文件名（与 Agent 侧 keyspace loader 同 schema）。</summary>
    public const string KeySpaceFileName = "agent.keyspace.generated.json";

    /// <summary>生成器版本标记（写入产物，供运行期/门禁自证「产物=生成器当前输出」）。</summary>
    public const string GeneratorVersion = "usharp-emitter/1";

    /// <summary>连接点命令目录产物文件名（<c>ui-commands-v2</c>，<c>readModels</c> 段随读模型声明落地）。</summary>
    public const string CommandsFileName = "ui-commands.generated.json";

    /// <summary>产物文件名（磁盘形态）。</summary>
    public const string ManifestFileName = "declared-manifest.json";

    /// <summary>round-spec 产物文件名。</summary>
    public const string RoundSpecFileName = "round-spec.generated.json";

    /// <summary>reference-schema 产物文件名。</summary>
    public const string ReferenceSchemaFileName = "reference-schema.generated.json";

    /// <summary>manifest 嵌入副本文件名（gzip；嵌入资源原样存储，故压缩后再进程序集）。</summary>
    public const string ManifestCompressedFileName = "declared-manifest.json.gz";

    /// <summary>manifest 顶层 schema 标识（哨兵，防手改/漂移）。</summary>
    public const string ManifestSchema = "declared-manifest-v1";

    // 机器产物：不缩进、省略 null、中文不转 \uXXXX（体积优先；门禁按 JSON 结构比对，不依赖缩进）。
    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions ReferenceSchemaOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>关系声明目录名（<c>[Relation]</c>/<c>[Invariant]</c>/<c>[Derive]</c> 的唯一声明面）。</summary>
    public const string RelationsDirName = "relations";

    /// <summary>从配置根构建（<c>Config</c> 闭包；目录缺失即空集，不兜底内容）。</summary>
    /// <param name="configRoot">配置根目录（含 <c>nodes</c> / <c>workflows</c> / <c>keyspaces</c> / <c>commands</c> / <c>state</c> / <c>relations</c>）。</param>
    public static DeclaredBundleArtifacts Build(string configRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configRoot);
        var closure = DeclarationClosures.Resolve(DeclarationClosureSpec.Bundle(configRoot));
        return BuildCore(closure, configRoot);
    }

    /// <summary>装载配置根下声明文档（闭包单一实现：<see cref="DeclarationClosures"/>；本方法仅保留兼容签名）。</summary>
    /// <param name="configRoot">配置根目录。</param>
    public static IReadOnlyList<(string Path, string Text)> LoadConfigDocuments(string configRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configRoot);
        return DeclarationClosures.Resolve(DeclarationClosureSpec.Bundle(configRoot))
            .Sources.Select(s => (s.Path, s.Text))
            .ToList();
    }

    /// <summary>由给定声明文档构建（<b>唯一一次</b>编译；调用方负责文档集，编译序 = 传入序）。</summary>
    /// <param name="documents">声明文档（路径 + 文本）。</param>
    public static DeclaredBundleArtifacts Build(IReadOnlyList<(string Path, string Text)> documents)
        => BuildCore(DeclarationClosures.FromDocuments(documents), null);

    private static DeclaredBundleArtifacts BuildCore(DeclarationClosure closure, string? configRoot)
    {
        ArgumentNullException.ThrowIfNull(closure);

        var compilation = UsharpCompilationHost.Shared.Resolve(closure);
        var fingerprint = closure.Fingerprint;
        var nodes = UsharpNodePortReader.Read(compilation.Declarations);
        var manifest = BuildManifest(compilation, nodes, fingerprint);
        var roundSpec = BuildRoundSpec(compilation);
        var referenceSchema = SerializeReferenceSchema(UsharpReferenceSchemaEmitter.Emit(compilation));
        var keySpaceJson = UsharpKeySpaceEmitter.Emit(
            compilation, DeclarationArtifactFingerprint.OfKeyspaces(closure.Sources), GeneratorVersion);
        var commandsJson = UsharpCommandEmitter.Emit(compilation);

        return new DeclaredBundleArtifacts(
            JsonSerializer.Serialize(manifest, ManifestOptions),
            JsonSerializer.Serialize(roundSpec, ManifestOptions),
            referenceSchema,
            keySpaceJson,
            commandsJson,
            fingerprint,
            compilation.Errors.Count,
            closure.Sources.Select(s => s.Path).ToList(),
            compilation.Diagnostics);
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
        File.WriteAllText(Path.Combine(outputDir, KeySpaceFileName), artifacts.KeySpaceJson, Encoding.UTF8);
        File.WriteAllText(Path.Combine(outputDir, CommandsFileName), artifacts.CommandsJson, Encoding.UTF8);
        WriteGzip(Path.Combine(outputDir, ManifestCompressedFileName), artifacts.ManifestJson);
    }

    /// <summary>源指纹：委托 <see cref="DeclarationArtifactFingerprint"/>（唯一实现）。</summary>
    /// <param name="documents">声明文档。</param>
    public static string Fingerprint(IEnumerable<(string Path, string Text)> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return DeclarationArtifactFingerprint.Of(
            documents.Select(d => new UsharpSourceFile(d.Path, d.Text ?? string.Empty, null)));
    }

    private static void WriteGzip(string path, string content)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: false);
        using var writer = new StreamWriter(gzip, new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>引用词表 Schema → 紧凑 JSON；SourceFile 归一为文件名（去机器相关绝对路径）。</summary>
    private static string SerializeReferenceSchema(ReferenceSchema schema)
    {
        schema.Nodes = schema.Nodes
            .Select(n => n with { SourceFile = Path.GetFileName(n.SourceFile) }).ToList();
        schema.Edges = schema.Edges
            .Select(e => e with { SourceFile = Path.GetFileName(e.SourceFile) }).ToList();
        schema.Constraints = schema.Constraints
            .Select(c => c with { SourceFile = Path.GetFileName(c.SourceFile) }).ToList();

        return JsonSerializer.Serialize(schema, ReferenceSchemaOptions);
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
                    spec?.OutputPorts ?? Array.Empty<string>(),
                    spec?.Executor));
            }

            flows.Add(new ManifestFlow(
                FileName(flow.SourceFile), flow.FlowId, flow.OverSpaceId, flow.SuccessCriterion, steps));
        }

        // functions = 声明面全部 fn（nodes 目录 + workflow 内联 [Node] fn=）单源：
        // 只取 nodes.Values 会漏掉 flows 内联声明的 fn（capability_* / signal_observe 等），
        // 使 E13「fn 常量 ⇄ manifest.functions」对账产生假红。
        var functions = nodes.Values
            .Select(n => n.FuncName)
            .Concat(flows.SelectMany(f => f.Steps).Select(s => s.Fn))
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        // 绑定清单（R5）：声明 ↔ 实现面的冻结视图（flowId/stepKey/fn/ports/executor + 源指纹）；
        // 域标识由 Agent 侧按声明域注册表绑定，框架层不持有域词。
        var bindings = flows
            .SelectMany(flow => flow.Steps.Select(step => new ManifestBinding(
                flow.FlowId, step.Key, step.Fn, step.Inports, step.Outports, step.Executor)))
            .OrderBy(b => b.FlowId, StringComparer.Ordinal)
            .ThenBy(b => b.StepKey, StringComparer.Ordinal)
            .ToList();

        // nodes 不落盘：steps 已承载 fn/inports/outports（消费方只读 flows.functions.vocabulary/ports）。
        return new ManifestDocument(
            ManifestSchema,
            fingerprint,
            flows,
            functions,
            new ManifestPorts(
                Distinct(nodes.Values.SelectMany(n => n.InputPorts)),
                Distinct(nodes.Values.SelectMany(n => n.OutputPorts))),
            bindings,
            BuildVocabulary(compilation),
            BuildReadModels(compilation),
            BuildConstants(compilation));
    }

    /// <summary>常量单元（G5）：<c>[Const]</c> 声明 → manifest <c>constants</c> 段（序 = target/valueType 升序）。</summary>
    private static IReadOnlyList<ManifestConstant> BuildConstants(UsharpCompilation compilation)
        => UsharpConstantsEmitter.Emit(compilation)
            .Select(u => new ManifestConstant(
                u.Target, u.Kind, u.Modifier, u.ValueType,
                u.Members.Select(m => new ManifestConstantMember(m.Name, m.Value)).ToList()))
            .ToList();

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

    /// <summary>round-spec 紧凑视图：flowId → 步骤表（键/执行体/入出端口/后继），不落 NodeConfig 样板。</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<ManifestRoundStep>> BuildRoundSpec(
        UsharpCompilation compilation)
    {
        var spec = new Dictionary<string, IReadOnlyList<ManifestRoundStep>>(StringComparer.Ordinal);
        foreach (var flow in compilation.Flows)
        {
            if (string.IsNullOrWhiteSpace(flow.FlowId)) continue;
            var emitted = UsharpRoundSpecEmitter.Emit(compilation, flow.FlowId);
            if (emitted.Spec is null || emitted.HasErrors || emitted.Spec.Steps.Count == 0) continue;

            var steps = new List<ManifestRoundStep>(emitted.Spec.Steps.Count);
            for (var i = 0; i < emitted.Spec.Steps.Count; i++)
            {
                var step = emitted.Spec.Steps[i];
                var next = i + 1 < emitted.Spec.Steps.Count
                    ? emitted.Spec.Steps[i + 1].EffectiveNodeId
                    : null;
                steps.Add(new ManifestRoundStep(
                    step.EffectiveNodeId,
                    step.FuncName,
                    step.ParameterKeys,
                    step.ResultKeys,
                    next));
            }
            spec[flow.FlowId] = steps;
        }
        return spec;
    }

    /// <summary>读模型声明段（声明序；漂移守卫按此段对账）。</summary>
    private static IReadOnlyList<ManifestReadModel> BuildReadModels(UsharpCompilation compilation)
    {
        compilation.TryGetFacet<IReadOnlyList<UsharpSegmentDecl>>(out var segments);
        var byKey = new Dictionary<string, UsharpSegmentDecl>(StringComparer.Ordinal);
        if (segments is not null)
            foreach (var segment in segments) byKey[segment.Key] = segment;

        return compilation.ReadModels
            .Select(m => new ManifestReadModel(
                m.ModelId, m.Sources, m.Version, m.TtlMs, m.Projector, m.Args,
                m.Fields.Count == 0 ? null : BuildFields(m, byKey)))
            .ToList();
    }

    /// <summary>字段表（序 = <c>fields=</c> 列出顺序；<c>from</c> = 段键）。</summary>
    private static IReadOnlyList<ManifestReadModelField> BuildFields(
        UsharpReadModelDecl model, IReadOnlyDictionary<string, UsharpSegmentDecl> byKey)
        => model.Fields
            .Where(byKey.ContainsKey)
            .Select(key =>
            {
                var segment = byKey[key];
                return new ManifestReadModelField(
                    segment.Field, segment.Type, segment.Order,
                    string.Equals(segment.Requirement, "required", StringComparison.OrdinalIgnoreCase),
                    segment.Key);
            })
            .ToList();

    private static IReadOnlyList<string> Distinct(IEnumerable<string> values)
        => values.Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

    private static string FileName(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileName(path!);

    // ---- manifest 结构（camelCase 序列化；键名与方案 §4.1 一致）----
    private sealed record ManifestDocument(
        string Schema, string Fingerprint, IReadOnlyList<ManifestFlow> Flows,
        IReadOnlyList<string> Functions, ManifestPorts Ports,
        IReadOnlyList<ManifestBinding> Bindings,
        IReadOnlyList<ManifestVocabulary> Vocabulary,
        IReadOnlyList<ManifestReadModel> ReadModels,
        IReadOnlyList<ManifestConstant> Constants);

    /// <summary>成员级声明单元（<c>[Const]</c>；生成器按此段回填 <c>partial</c> 成员）。</summary>
    private sealed record ManifestConstant(
        string Target, string Kind, string Modifier, string ValueType,
        IReadOnlyList<ManifestConstantMember> Members);

    /// <summary>常量成员（名 + 值原文；逐字输出，禁改写）。</summary>
    private sealed record ManifestConstantMember(string Name, string Value);

    /// <summary>读模型声明条目（<c>[ReadModel]</c> 语法单元；漂移守卫按此段对账）。</summary>
    private sealed record ManifestReadModel(
        string Model, IReadOnlyList<string> Sources, string Version, int? Ttl,
        string? Projector, IReadOnlyList<string> Args,
        IReadOnlyList<ManifestReadModelField>? Fields);

    /// <summary>读模型字段条目（<c>fields=</c> → 段声明解析结果；<c>From</c> = 段键）。</summary>
    private sealed record ManifestReadModelField(
        string Name, string Type, int Order, bool Required, string From);

    /// <summary>绑定条目（冻结产物）：声明侧标识 + 实现面函数名 + 端口契约（结构事实）。</summary>
    private sealed record ManifestBinding(
        string FlowId, string StepKey, string Fn,
        IReadOnlyList<string> Inports, IReadOnlyList<string> Outports, string? Executor);

    private sealed record ManifestVocabulary(string Space, string Kind, IReadOnlyList<string> Keys);

    private sealed record ManifestFlow(
        string File, string FlowId, string? Over, string? Success, IReadOnlyList<ManifestStep> Steps);

    private sealed record ManifestStep(
        string Key, string Fn, IReadOnlyList<string> Inports, IReadOnlyList<string> Outports, string? Executor = null);

    private sealed record ManifestPorts(IReadOnlyList<string> Inports, IReadOnlyList<string> Outports);

    // ---- round-spec 紧凑结构（flowId → 步骤表；不落 NodeConfig 样板）----
    private sealed record ManifestRoundStep(
        string Key, string Fn, IReadOnlyList<string> Inports, IReadOnlyList<string> Outports, string? Next);
}
