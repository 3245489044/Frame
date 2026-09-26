using System.Text.Json.Nodes;
using Modules.Node.Diagnostics;

namespace Modules.Node.Instances;

/// <summary>
/// 实例类型声明装载器（G-1 声明面）—— 解析 Kind 声明文档并注册到 <see cref="InstanceKindRegistry"/>。
/// 复用实例级诊断端口 <see cref="INodeDiagnosticsSink"/> 与框架 JSON 宽容解析（<c>NodeJson</c>，单源）。
/// <para>
/// 装载**两遍**（同批声明可互相引用）：① 解析（字段非法 ⇒ Error + 跳过该文档，禁静默兜底）；
/// ② 引用校验（级联目标 Kind 须同批声明或已注册 ⇒ 否则 Error 且**不注册**该文档）+ 注册。
/// </para>
/// <para>
/// 声明字段：<c>kind</c> / <c>identityTemplate</c> / <c>store</c>（必填）；<c>placements</c>（位置 id 引用集，可为空 ——
/// 层级 Kind 经 owns/link 边表达，位置由承载 Kind 声明）；<c>cascades</c>（<c>kind</c>+<c>cascade</c>）；
/// <c>retention</c>（keep / sealAfterClose / archiveAfterSeal，缺省 keep）；<c>runtimeBound</c>（bool，缺省 false）。
/// </para>
/// <para>
/// <c>identityTemplate</c> **仅接受引用式**（<c>{声明文件}#{字段路径}</c>，D8①）：被引用者可为
/// ① 节点域声明（含 <c>placements</c>，取其 <c>identity</c>）或 ② **仅身份声明**（无 <c>placements</c>，
/// 仅承载 <c>identity</c>）；内联模板（无 <c>#</c>）在有读取器时**拒**（内联即复制模板文本，破坏单源）。
/// </para>
/// </summary>
public sealed class InstanceFrameworkSpecLoader
{
    private readonly InstanceKindRegistry _registry;
    private readonly INodeDiagnosticsSink _diagnostics;
    private readonly Func<string, string?>? _declarationReader;

    /// <param name="registry">Kind 注册表（装配期写入）。</param>
    /// <param name="diagnostics">诊断出口。</param>
    /// <param name="declarationReader">
    /// 声明文件读取器（<c>{声明文件} → 文本</c>）—— 供 <c>#</c> 引用式身份模板的**可解析性校验**用；
    /// null = 不校验引用（纯内存装载 / 单元测试场景）。框架不感知识别路径，读取能力由调用方注入（守零依赖）。
    /// </param>
    public InstanceFrameworkSpecLoader(
        InstanceKindRegistry registry,
        INodeDiagnosticsSink? diagnostics = null,
        Func<string, string?>? declarationReader = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
        _declarationReader = declarationReader;
    }

    /// <summary>装载单份声明（成功注册 ⇒ true；非法/引用不满足 ⇒ false + 诊断）。</summary>
    public bool TryLoad(string sourceId, string json) => LoadAll(new[] { (sourceId, json) }) == 1;

    /// <summary>批量装载（按声明序）；返回成功注册的 Kind 数。同批内级联可互相引用。</summary>
    public int LoadAll(IEnumerable<(string SourceId, string Json)> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var parsed = new List<(string SourceId, InstanceKindSpec Spec)>();
        foreach (var (sourceId, json) in documents)
        {
            var spec = Parse(sourceId, json);
            if (spec is not null)
                parsed.Add((sourceId, spec));
        }

        // 第 2 遍：级联引用须落在「同批声明 ∪ 已注册」内（禁悬空级联：静默不级联是数据缺陷）。
        var declared = new HashSet<string>(_registry.Kinds, StringComparer.Ordinal);
        foreach (var (_, spec) in parsed)
            declared.Add(spec.Kind);

        var registered = 0;
        foreach (var (sourceId, spec) in parsed)
        {
            var unknown = spec.Cascades
                .Select(static rule => rule.Kind)
                .Where(target => !declared.Contains(target))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (unknown.Length > 0)
            {
                Fail("instance.spec-cascade-unknown",
                    $"级联目标 Kind 未声明：{string.Join(", ", unknown)}（级联须落在已声明 Kind 内）",
                    $"{sourceId}|{spec.Kind}");
                continue;
            }

            if (_registry.TryGet(spec.Kind, out _))
            {
                Fail("instance.spec-kind-duplicate", "Kind 已注册（Kind 语义唯一）", $"{sourceId}|{spec.Kind}");
                continue;
            }

            _registry.Register(spec);
            registered++;
        }

        return registered;
    }

    /// <summary>解析单份声明（字段非法 ⇒ Error + null：不产生半成品 spec）。</summary>
    private InstanceKindSpec? Parse(string sourceId, string json)
    {
        if (!NodeJson.TryParse(json, out var node, out var parseError) || node is not JsonObject obj)
        {
            Fail("instance.spec-invalid", $"声明解析失败（{parseError ?? "根节点非 JSON 对象"}）", sourceId);
            return null;
        }

        var kind = TextOf(obj, "kind");
        var identityTemplate = TextOf(obj, "identityTemplate");
        var storeKind = TextOf(obj, "store");
        if (string.IsNullOrWhiteSpace(kind) ||
            string.IsNullOrWhiteSpace(identityTemplate) ||
            string.IsNullOrWhiteSpace(storeKind))
        {
            Fail("instance.spec-invalid", "缺必填字段（kind / identityTemplate / store）", sourceId);
            return null;
        }

        if (!TryParseRetention(TextOf(obj, "retention"), out var retention))
        {
            Fail("instance.spec-invalid",
                "retention 非法（可取值: keep / sealAfterClose / archiveAfterSeal）", $"{sourceId}|{kind}");
            return null;
        }

        // 身份模板引用可解析性（禁悬空引用）：引用式模板的**单源**在位置声明层，本层只引用不复制；
        // 悬空即数据缺陷（身份无来源 ⇒ 实例身份不可求值），故拒绝注册而非静默降级。
        if (!TryValidateTemplateReference(identityTemplate!, out var referenceError))
        {
            Fail("instance.spec-template-unresolved",
                $"身份模板引用不可解析（{referenceError}）", $"{sourceId}|{kind}");
            return null;
        }

        var cascades = new List<CascadeRule>();
        if (obj.TryGetPropertyValue("cascades", out var cascadeNode) && cascadeNode is JsonArray cascadeArray)
        {
            foreach (var element in cascadeArray)
            {
                if (element is not JsonObject cascadeObject)
                {
                    Fail("instance.spec-invalid", "cascades 元素须为对象 { kind, cascade }", $"{sourceId}|{kind}");
                    return null;
                }

                var target = TextOf(cascadeObject, "kind");
                if (string.IsNullOrWhiteSpace(target) ||
                    !TryParseCascade(TextOf(cascadeObject, "cascade"), out var cascadeKind))
                {
                    Fail("instance.spec-invalid",
                        "级联项非法（须为 { kind, cascade }，cascade ∈ holds / refs / derived）", $"{sourceId}|{kind}");
                    return null;
                }

                cascades.Add(new CascadeRule(target!, cascadeKind));
            }
        }

        return new InstanceKindSpec(
            kind!,
            identityTemplate!,
            StringsOf(obj, "placements"),
            storeKind!,
            cascades,
            retention,
            BoolOf(obj, "runtimeBound"));
    }

    private void Fail(string code, string message, string? subject) =>
        _diagnostics.Report(new NodeDiagnostic(code, NodeDiagnosticSeverity.Error, message, subject));

    /// <summary>
    /// 校验引用式身份模板（<c>{声明文件}#{字段路径}</c>）可解析：文件可达 + 字段路径存在 + 取值为非空字符串。
    /// <para>
    /// 两形态均接受（D8①）：① **节点域声明**（含 <c>placements</c>，取其 <c>identity</c>）；
    /// ② **仅身份声明**（无 <c>placements</c>，仅承载 <c>identity</c>）—— 供各 Kind 自持身份模板
    /// （如「会话容器 = 落盘单位」，身份不随轮次变化），框架不要求被引用者是位置声明。
    /// </para>
    /// <para>
    /// 判据边界：无读取器（测试/纯内存）⇒ 不判（返回 true）；模板**不含 <c>#</c>（内联）**⇒ 有读取器时**拒**
    /// （内联即在本层复制模板文本，违反「身份模板单源在声明面」）。
    /// </para>
    /// <para>字段路径文法：<c>a.b</c> 逐层属性；<c>a[].b</c> 数组投影（要求数组非空且首元素含后续字段）。</para>
    /// </summary>
    private bool TryValidateTemplateReference(string template, out string? reason)
    {
        reason = null;
        if (_declarationReader is null)
            return true;

        var hash = template.IndexOf('#');
        if (hash < 0)
        {
            reason = $"内联身份模板不允许（须为引用式 {{声明文件}}#{{字段}}；身份模板单源在声明面）：{template}";
            return false;
        }

        var file = template[..hash].Trim();
        var fieldPath = template[(hash + 1)..].Trim();
        if (file.Length == 0 || fieldPath.Length == 0)
        {
            reason = $"引用形态须为 {{声明文件}}#{{字段}}：{template}";
            return false;
        }

        var json = _declarationReader(file);
        if (string.IsNullOrWhiteSpace(json))
        {
            reason = $"声明文件不可达：{file}";
            return false;
        }

        if (!NodeJson.TryParse(json, out var node, out var parseError) || node is not JsonObject root)
        {
            reason = $"声明文件解析失败：{file}（{parseError ?? "根节点非 JSON 对象"}）";
            return false;
        }

        return TryResolveFieldPath(root, fieldPath, out reason);
    }

    /// <summary>字段路径求值（只判存在性，不取值；空段/缺属性/空数组/非对象元素 ⇒ false + 原因）。</summary>
    private static bool TryResolveFieldPath(JsonObject root, string fieldPath, out string? reason)
    {
        reason = null;
        JsonNode? current = root;
        foreach (var raw in fieldPath.Split('.'))
        {
            var segment = raw;
            var projected = segment.EndsWith("[]", StringComparison.Ordinal);
            if (projected)
                segment = segment[..^2];

            if (segment.Length == 0)
            {
                reason = $"字段路径含空段：{fieldPath}";
                return false;
            }

            if (current is not JsonObject holder ||
                !holder.TryGetPropertyValue(segment, out var next) || next is null)
            {
                reason = $"字段不存在：{segment}（路径 {fieldPath}）";
                return false;
            }

            if (!projected)
            {
                current = next;
                continue;
            }

            if (next is not JsonArray array || array.Count == 0)
            {
                reason = $"数组投影目标非非空数组：{segment}（路径 {fieldPath}）";
                return false;
            }

            if (array[0] is not JsonObject element)
            {
                reason = $"数组投影元素非对象：{segment}（路径 {fieldPath}）";
                return false;
            }

            current = element;
        }

        // 取值须为非空字符串：身份模板是**可求值文本**，空值/对象/数组即无来源（悬空 ⇒ 拒，不静默降级）。
        if (current is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
        {
            reason = $"引用目标非非空字符串（路径 {fieldPath}）";
            return false;
        }

        return true;
    }

    private static string? TextOf(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    private static bool BoolOf(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out var node) &&
        node is JsonValue value &&
        value.TryGetValue<bool>(out var flag) &&
        flag;

    private static IReadOnlyList<string> StringsOf(JsonObject obj, string name)
    {
        if (!obj.TryGetPropertyValue(name, out var node) || node is not JsonArray array)
            return Array.Empty<string>();

        return array
            .Select(static item => item is JsonValue value && value.TryGetValue<string>(out var text) ? text : null)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .Select(static text => text!)
            .ToArray();
    }

    private static bool TryParseRetention(string? text, out InstanceRetention retention)
    {
        retention = InstanceRetention.Keep;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        return Enum.TryParse(text, ignoreCase: true, out retention) && Enum.IsDefined(retention);
    }

    private static bool TryParseCascade(string? text, out InstanceCascadeKind kind)
    {
        kind = InstanceCascadeKind.Holds;
        return !string.IsNullOrWhiteSpace(text) &&
               Enum.TryParse(text, ignoreCase: true, out kind) &&
               Enum.IsDefined(kind);
    }
}
