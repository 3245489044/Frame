using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Plate.JsonMethod.Serialization;
using Plate.JsonMethod.Utilities;
using Plate.JsonMethod.Validation;

namespace Modules.Parsing.Invocation;

/// <summary>
/// 调用面声明目录（唯一声明面 + 唯一加载入口）。
/// <para>数据源 = 嵌入资源 <c>invocation.specs.json</c>；JSON 读取/校验/反序列化全部复用 <c>Plate.JsonMethod</c>，本类不新增 JSON 逻辑。</para>
/// <para>加载链固定：重复键扫描 → Schema 子集结构校验 → 强类型反序列化 → 语义归一（索引）。</para>
/// </summary>
public static class InvocationSpecCatalog
{
    private const string SpecsResource = "invocation.specs.json";
    private const string SchemaResource = "invocation.specs.v1.schema.json";

    private static readonly Lazy<(IReadOnlyList<InvocationSpec> Specs, IReadOnlyList<string> Issues, int? DefaultTimeoutMs)> Loaded =
        new(LoadCore, isThreadSafe: true);

    /// <summary>全部声明（按声明序）。</summary>
    public static IReadOnlyList<InvocationSpec> All => Loaded.Value.Specs;

    /// <summary>加载期问题（重复键 / Schema 违规 / 语义不完整）；非空 ⇒ 声明面不合格。</summary>
    public static IReadOnlyList<string> LoadIssues => Loaded.Value.Issues;

    /// <summary>命令级默认预算（ms）；未声明 ⇒ null（由消费方决定，不在这里写死）。</summary>
    public static int? DefaultTimeoutMs => Loaded.Value.DefaultTimeoutMs;

    /// <summary>声明面是否合格（加载零问题且非空）。</summary>
    public static bool IsWellFormed => LoadIssues.Count == 0 && All.Count > 0;

    /// <summary>按动作（+ 目标首 token 判子动作）解析声明；未命中 ⇒ false。</summary>
    public static bool TryResolve(string? action, string? target, out InvocationSpec spec)
    {
        spec = null!;
        if (string.IsNullOrWhiteSpace(action)) return false;

        var candidates = All.Where(s => string.Equals(s.Action, action.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0) return false;

        if (candidates.Length == 1 && candidates[0].SubAction is null)
        {
            spec = candidates[0];
            return true;
        }

        var sub = InvocationTokens.First(target);
        var hit = candidates.FirstOrDefault(c => string.Equals(c.SubAction, sub, StringComparison.OrdinalIgnoreCase));
        if (hit is null) return false;

        spec = hit;
        return true;
    }

    /// <summary>目标串切分（委托唯一分词实现）。</summary>
    public static IReadOnlyList<string> Tokenize(string? target) => InvocationTokens.Tokenize(target);

    /// <summary>目标串首 token。</summary>
    public static string? FirstToken(string? target) => InvocationTokens.First(target);

    /// <summary>资源清单（诊断用：确认嵌入资源名漂移）。</summary>
    public static IReadOnlyList<string> ResourceNames
        => typeof(InvocationSpecCatalog).Assembly.GetManifestResourceNames();

    private static (IReadOnlyList<InvocationSpec>, IReadOnlyList<string>, int?) LoadCore()
    {
        var issues = new List<string>();
        var json = ReadResource(SpecsResource);
        if (json is null)
        {
            issues.Add($"缺少声明资源：{SpecsResource}");
            return (Array.Empty<InvocationSpec>(), issues, null);
        }

        // ① 原文层重复键（对象层会静默合并同名键）
        foreach (var duplicate in JsonDuplicateKeyScanner.Scan(json))
            issues.Add($"{SpecsResource}: 重复键 '{duplicate}'（同对象内同名，后者静默覆盖前者）");

        // ② Schema 子集结构校验（只返回违规，不抛）
        var schema = ReadResource(SchemaResource);
        if (schema is null)
            issues.Add($"缺少 Schema 资源：{SchemaResource}");
        else
            issues.AddRange(JsonSchemaSubsetValidator.Validate(json, schema, SpecsResource));

        // ③ 强类型反序列化（选项单源）
        InvocationSpecDocument? document;
        try
        {
            document = JsonSerializationService.Instance.Deserialize<InvocationSpecDocument>(json, JsonContext.ScriptCommand);
        }
        catch (Exception ex)
        {
            issues.Add($"{SpecsResource}: 反序列化失败 — {ex.Message}");
            return (Array.Empty<InvocationSpec>(), issues, null);
        }

        if (document?.Specs is null || document.Specs.Count == 0)
        {
            issues.Add($"{SpecsResource}: 未声明任何命令（判据无标的）");
            return (Array.Empty<InvocationSpec>(), issues, document?.DefaultTimeoutMs);
        }

        // ④ 语义归一（只处理内存模型）
        var specs = new List<InvocationSpec>(document.Specs.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < document.Specs.Count; i++)
        {
            var entry = document.Specs[i];
            var where = $"{SpecsResource}[{i}]";

            if (string.IsNullOrWhiteSpace(entry.Action))
            {
                issues.Add($"{where}: 缺少 action");
                continue;
            }

            var space = NameSpaceId.None;
            if (!string.IsNullOrWhiteSpace(entry.TargetSpace)
                && (!Enum.TryParse(entry.TargetSpace, ignoreCase: true, out space) || !Enum.IsDefined(space)))
            {
                issues.Add($"{where}: 未知 targetSpace '{entry.TargetSpace}'");
                continue;
            }

            var argSpaces = BuildArgSpaces(entry, where, issues, out var bad);
            if (bad) continue;

            var key = $"{entry.Action.Trim()}/{entry.SubAction?.Trim() ?? "*"}";
            if (!seen.Add(key))
            {
                issues.Add($"{where}: 重复声明 '{key}'（同动作同子动作只能声明一次）");
                continue;
            }

            specs.Add(new InvocationSpec(
                entry.Action.Trim(),
                string.IsNullOrWhiteSpace(entry.SubAction) ? null : entry.SubAction.Trim(),
                space,
                entry.Args is { Count: > 0 } ? entry.Args : null,
                entry.AllowProbeArgs ?? false,
                argSpaces,
                entry.MinTargetTokens ?? 1));
        }

        return (specs, issues, document.DefaultTimeoutMs);
    }

    private static IReadOnlyDictionary<string, NameSpaceId>? BuildArgSpaces(
        InvocationSpecEntry entry,
        string where,
        List<string> issues,
        out bool bad)
    {
        bad = false;
        if (entry.ArgSpaces is null || entry.ArgSpaces.Count == 0) return null;

        var argSpaces = new Dictionary<string, NameSpaceId>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entry.ArgSpaces)
        {
            if (!Enum.TryParse<NameSpaceId>(value, ignoreCase: true, out var argSpace) || !Enum.IsDefined(argSpace))
            {
                issues.Add($"{where}: 参数 '{key}' 的空间 '{value}' 未登记");
                bad = true;
                continue;
            }

            argSpaces[key] = argSpace;
        }

        return bad ? null : argSpaces;
    }

    private static string? ReadResource(string logicalName)
    {
        var assembly = typeof(InvocationSpecCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(logicalName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

}
