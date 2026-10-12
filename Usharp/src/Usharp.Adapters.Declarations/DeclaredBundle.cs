using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// <b>冻结声明产物读取器</b>（G1）：从**嵌入资源**读 <see cref="DeclaredBundleBuilder"/> 产出的三份 JSON
/// （manifest / round-spec / reference-schema），提供运行期零磁盘、零编译、零目录枚举的声明事实源。
/// <para>
/// 消费方（<c>RoundSpecLoader</c> / <c>UsharpNodeSpecSource</c> / <c>UsharpWorkflowRegistrar</c> /
/// <c>DeclaredReferenceVocabulary</c> / <c>WorkflowFunctionRefValidator</c>）一律经本类读产物，
/// 不再各自 <c>UsharpCompilation.Compile</c>（收敛见方案 §4.1）。
/// </para>
/// <para>缺件语义：未装载（<see cref="IsLoaded"/>=false）而非抛 —— 失败姿态统一由 <c>IDeclarationGate</c>（G3）裁决，
/// 本类不自持策略。</para>
/// </summary>
public sealed class DeclaredBundle
{
    /// <summary>manifest 嵌入资源逻辑名（csproj <c>LogicalName</c> 须与此一致）。</summary>
    public const string ManifestResourceName = "DeclaredBundle.Manifest";

    /// <summary>round-spec 嵌入资源逻辑名。</summary>
    public const string RoundSpecResourceName = "DeclaredBundle.RoundSpec";

    /// <summary>reference-schema 嵌入资源逻辑名。</summary>
    public const string ReferenceSchemaResourceName = "DeclaredBundle.ReferenceSchema";

    /// <summary>未装载空实例（嵌入资源缺省；不伪造事实）。</summary>
    public static readonly DeclaredBundle Empty = new(string.Empty, string.Empty, string.Empty, false);

    /// <summary>顶层清单 JSON（camelCase；schema/fingerprint/flows/nodes/functions/ports）。</summary>
    public string ManifestJson { get; }

    /// <summary>逐 <c>[Flow]</c> 骨架链 JSON（flowId → chain）。</summary>
    public string RoundSpecJson { get; }

    /// <summary>引用词表 Schema JSON。</summary>
    public string ReferenceSchemaJson { get; }

    /// <summary>是否已从嵌入资源装载（false ⇒ 调用方按既定门控回退）。</summary>
    public bool IsLoaded { get; }

    /// <summary>源指纹（从 manifest 解析；缺省空串）。</summary>
    public string SourceFingerprint { get; }

    private DeclaredBundle(string manifestJson, string roundSpecJson, string referenceSchemaJson, bool isLoaded)
    {
        ManifestJson = manifestJson ?? string.Empty;
        RoundSpecJson = roundSpecJson ?? string.Empty;
        ReferenceSchemaJson = referenceSchemaJson ?? string.Empty;
        IsLoaded = isLoaded;
        SourceFingerprint = ExtractFingerprint(ManifestJson);
    }

    /// <summary>从程序集嵌入资源装载（缺件 ⇒ <see cref="Empty"/> 语义，返回未装载实例）。</summary>
    /// <param name="assembly">承载产物的程序集（缺省 = 本类所在程序集）。</param>
    public static DeclaredBundle Load(Assembly? assembly = null)
    {
        assembly ??= typeof(DeclaredBundle).Assembly;

        var manifest = ReadResource(assembly, ManifestResourceName);
        var roundSpec = ReadResource(assembly, RoundSpecResourceName);
        var referenceSchema = ReadResource(assembly, ReferenceSchemaResourceName);

        var loaded = manifest is not null || roundSpec is not null || referenceSchema is not null;
        return new DeclaredBundle(manifest ?? string.Empty, roundSpec ?? string.Empty, referenceSchema ?? string.Empty, loaded);
    }

    /// <summary>从磁盘目录装载（开发态 / 重生成产物：读取 <see cref="DeclaredBundleBuilder"/> 的三份文件名）。</summary>
    /// <param name="directory">产物目录。</param>
    public static DeclaredBundle LoadFromDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return Empty;

        var manifest = ReadFileIfExists(Path.Combine(directory, DeclaredBundleBuilder.ManifestFileName));
        var roundSpec = ReadFileIfExists(Path.Combine(directory, DeclaredBundleBuilder.RoundSpecFileName));
        var referenceSchema = ReadFileIfExists(Path.Combine(directory, DeclaredBundleBuilder.ReferenceSchemaFileName));

        var loaded = manifest is not null || roundSpec is not null || referenceSchema is not null;
        return new DeclaredBundle(manifest ?? string.Empty, roundSpec ?? string.Empty, referenceSchema ?? string.Empty, loaded);
    }

    /// <summary>解析 manifest 顶层 JSON（未装载 / 非法 ⇒ null）。</summary>
    public JsonDocument? ParseManifest()
    {
        if (string.IsNullOrWhiteSpace(ManifestJson)) return null;
        try { return JsonDocument.Parse(ManifestJson); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// 取词表（G4）：空间号 → 坐标名表 —— C# 侧词表字面量的**单一读取口**（替代散落字面量）。
    /// <para>未装载 / 空间未命中 ⇒ 空表（调用方按既定门控回退，不自持策略）。</para>
    /// </summary>
    /// <param name="spaceId">空间号（如 <c>HierarchyRef</c> / <c>PlacementRef</c>）。</param>
    public IReadOnlyList<string> Vocabulary(string spaceId)
    {
        if (string.IsNullOrWhiteSpace(spaceId)) return Array.Empty<string>();

        using var doc = ParseManifest();
        if (doc is null) return Array.Empty<string>();
        if (!doc.RootElement.TryGetProperty("vocabulary", out var vocab) || vocab.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        foreach (var space in vocab.EnumerateArray())
        {
            if (!space.TryGetProperty("space", out var s)
                || !string.Equals(s.GetString(), spaceId, StringComparison.Ordinal)) continue;
            if (!space.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            var result = new List<string>();
            foreach (var k in keys.EnumerateArray())
            {
                var v = k.GetString();
                if (!string.IsNullOrWhiteSpace(v)) result.Add(v!);
            }
            return result;
        }
        return Array.Empty<string>();
    }

    /// <summary>manifest 中的一条 <c>[Flow]</c> 步骤（步骤键 → 可执行体 + 端口）。</summary>
    /// <param name="FlowId">流号。</param>
    /// <param name="Key">步骤键。</param>
    /// <param name="Fn">可执行体函数名。</param>
    /// <param name="Inports">入端口键。</param>
    /// <param name="Outports">出端口键。</param>
    public sealed record DeclaredFlowStep(
        string FlowId, string Key, string Fn, IReadOnlyList<string> Inports, IReadOnlyList<string> Outports);

    /// <summary>
    /// 取全部 <c>[Flow]</c> 步骤（G3：启动期函数引用校验改读产物，替代逐文件 <c>Compile</c>）。
    /// <para>未装载 / 非法 ⇒ 空表（调用方回退旧路，不自持策略）。</para>
    /// </summary>
    public IReadOnlyList<DeclaredFlowStep> FlowSteps()
    {
        using var doc = ParseManifest();
        if (doc is null) return Array.Empty<DeclaredFlowStep>();
        if (!doc.RootElement.TryGetProperty("flows", out var flows) || flows.ValueKind != JsonValueKind.Array)
            return Array.Empty<DeclaredFlowStep>();

        var result = new List<DeclaredFlowStep>();
        foreach (var flow in flows.EnumerateArray())
        {
            var flowId = flow.TryGetProperty("flowId", out var fid) ? fid.GetString() ?? string.Empty : string.Empty;
            if (!flow.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array) continue;

            foreach (var step in steps.EnumerateArray())
            {
                var key = step.TryGetProperty("key", out var k) ? k.GetString() ?? string.Empty : string.Empty;
                var fn = step.TryGetProperty("fn", out var f) ? f.GetString() ?? string.Empty : string.Empty;
                result.Add(new DeclaredFlowStep(flowId, key, fn, ReadStringArray(step, "inports"), ReadStringArray(step, "outports")));
            }
        }
        return result;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            var v = item.GetString();
            if (!string.IsNullOrWhiteSpace(v)) list.Add(v!);
        }
        return list;
    }

    private static string ExtractFingerprint(string manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            return doc.RootElement.TryGetProperty("fingerprint", out var fp) ? fp.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException) { return string.Empty; }
    }

    private static string? ReadResource(Assembly assembly, string resourceName)
    {
        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception) { return null; }
    }

    private static string? ReadFileIfExists(string path)
        => File.Exists(path) ? File.ReadAllText(path) : null;
}
