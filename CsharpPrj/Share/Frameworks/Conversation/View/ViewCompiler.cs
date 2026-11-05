using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Share.Conversation.View;

/// <summary>编译输入（7 份声明的**文本**；由调用方读盘后传入，编译器自身无 IO）。</summary>
public sealed record ViewSources(
    string FrameKindsJson,
    string PresentationJson,
    string ReadPathsJson,
    string AnswerFactsJson,
    string UiRegionsJson,
    string GatherViewsJson,
    string? FoldJson = null);

/// <summary>编译产物（三张只读表 + 总指纹）。</summary>
public sealed record ViewCompiled(
    FrameTable Frames,
    ReadPlanTable Reads,
    ViewPlanTable Views,
    string Fingerprint);

/// <summary>
/// 声明编译器（纯函数）：7 份声明文本 → 3 张只读表。
/// <para>纪律：只做合并与校验，不做业务判断；跨表引用不可解析 ⇒ <c>errors</c> 非空且**不产出半套表**（fail-closed）。</para>
/// </summary>
public static class ViewCompiler
{
    /// <summary>编译产物 schema 版本（并入指纹，改格式即改指纹）。</summary>
    public const string Schema = "view-compiled-v1";

    /// <summary>空声明 FoldJson 时的兼容词表（新调用方须传 <c>ui/fold.json</c>；词表单源 = 该声明）。</summary>
    private static readonly string[] DefaultFoldWords = { "none", "round", "lead", "attach", "group" };

    /// <summary>编译 7 份声明文本。</summary>
    /// <param name="sources">声明文本。</param>
    /// <param name="errors">诊断（空 = 可用；非空 ⇒ 返回 null）。</param>
    public static ViewCompiled? Compile(ViewSources sources, out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var list = new List<string>();

        var frames = ParseFrames(sources.FrameKindsJson, list);
        var presentations = ParsePresentationClasses(sources.PresentationJson, list);
        var terminalPresentation = ParseTerminalPresentation(sources.PresentationJson);
        var sourcesList = ParseReadSources(sources.ReadPathsJson, list);
        var planBindings = ParsePlanBindings(sources.ReadPathsJson, list);
        var answer = ParseAnswerRule(sources.AnswerFactsJson, terminalPresentation, list);
        var foldWords = ParseFoldWords(sources.FoldJson, list);
        var views = ParseViews(sources.UiRegionsJson, sources.GatherViewsJson, foldWords, list);

        ValidateCross(frames, presentations, sourcesList, views, list);

        var reads = BuildReadPlans(views, sourcesList, answer, planBindings, list);

        errors = list;
        if (list.Count > 0) return null;

        var fingerprint = Fingerprint(frames, reads, views);
        return new ViewCompiled(
            frames with { Fingerprint = fingerprint },
            reads with { Fingerprint = fingerprint },
            views with { Fingerprint = fingerprint },
            fingerprint);
    }

    // ---------- frame-kinds.json ----------

    private static FrameTable ParseFrames(string json, List<string> errors)
    {
        var rows = new Dictionary<string, FrameRow>(StringComparer.Ordinal);
        if (!TryParse(json, "frame-kinds", errors, out var root)) return FrameTable.Empty;

        var fileOrdinal = ReadString(root, "ordinal") ?? "arrival";
        var subjectDefault = ReadString(root, "subjectDefault") ?? "root";
        if (!root.TryGetProperty("kinds", out var kinds) || kinds.ValueKind != JsonValueKind.Array)
        {
            errors.Add("[F1] frame-kinds.kinds 缺失或非数组");
            return FrameTable.Empty;
        }

        foreach (var entry in kinds.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var kind = ReadString(entry, "kind");
            var cls = ReadString(entry, "class");
            var presentation = ReadString(entry, "presentation");
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(cls) || string.IsNullOrWhiteSpace(presentation))
            {
                errors.Add($"[F1] frame-kinds 行缺 kind/class/presentation：{kind ?? "<null>"}");
                continue;
            }

            if (rows.ContainsKey(kind!))
            {
                errors.Add($"[F1] frame-kinds 行重复：{kind}");
                continue;
            }

            var anchor = ParseAnchor(ReadString(entry, "anchor"), kind!, errors);
            var fold = ReadString(entry, "fold") ?? "none";
            // 帧级 fold 档（none/attach/round）与视图 content.fold 模式（round-closed…）是两个轴：
            // 本处用帧档词表；视图模式词表来自 ui/fold.json（见 ParseFoldWords）。
            if (!DefaultFoldWords.Contains(fold, StringComparer.Ordinal))
            {
                errors.Add($"[F8] frame-kinds 行 {kind} 的 fold '{fold}' 不在受控词（{string.Join('/', DefaultFoldWords)}）");
            }

            rows[kind!] = new FrameRow(
                kind!,
                cls!,
                presentation!,
                anchor,
                ReadBool(entry, "archive"),
                ReadStringArray(entry, "views"),
                fold,
                ReadString(entry, "ordinal") ?? fileOrdinal,
                ReadString(entry, "subject") ?? subjectDefault);
        }

        return new FrameTable(rows, string.Empty);
    }

    private static AnchorPolicy ParseAnchor(string? word, string kind, List<string> errors)
        => word switch
        {
            "always" => AnchorPolicy.Always,
            "if-answer" => AnchorPolicy.IfAnswer,
            "never" => AnchorPolicy.Never,
            _ => AddError(errors, $"[F8] frame-kinds 行 {kind} 的 anchor '{word}' 不在受控词（always/if-answer/never）"),
        };

    // ---------- presentation.json ----------

    private static IReadOnlyList<string> ParsePresentationClasses(string json, List<string> errors)
    {
        if (!TryParse(json, "presentation", errors, out var root)) return Array.Empty<string>();
        if (!root.TryGetProperty("classes", out var classes) || classes.ValueKind != JsonValueKind.Object)
        {
            errors.Add("[F3] presentation.classes 缺失或非对象");
            return Array.Empty<string>();
        }

        return classes.EnumerateObject().Select(static p => p.Name).ToArray();
    }

    private static string ParseTerminalPresentation(string json)
    {
        if (!TryParse(json, "presentation", null, out var root)) return string.Empty;
        if (!root.TryGetProperty("facts", out var facts) || facts.ValueKind != JsonValueKind.Object) return string.Empty;
        return ReadString(facts, "terminal") ?? string.Empty;
    }

    // ---------- read-paths.json ----------

    private static IReadOnlyList<ReadSource> ParseReadSources(string json, List<string> errors)
    {
        var list = new List<ReadSource>();
        if (!TryParse(json, "read-paths", errors, out var root)) return list;
        if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Array)
        {
            errors.Add("[R1] read-paths.paths 缺失或非数组");
            return list;
        }

        var index = 0;
        foreach (var entry in paths.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var domain = ReadString(entry, "domain");
            if (string.IsNullOrWhiteSpace(domain))
            {
                errors.Add($"[R1] read-paths 第 {index} 条缺 domain");
                index++;
                continue;
            }

            var facts = ReadStringArray(entry, "facts");
            if (facts.Count == 0)
            {
                errors.Add($"[R1] read-paths 域 {domain} 的 facts 为空（读路径无事实声明）");
            }

            var role = ReadString(entry, "role") ?? ReadSourceRoles.Spine;
            if (role is not (ReadSourceRoles.Spine or ReadSourceRoles.Supplement))
            {
                errors.Add($"[R1] read-paths 域 {domain} 的 role '{role}' 不在受控词（{ReadSourceRoles.Spine}/{ReadSourceRoles.Supplement}）");
                role = ReadSourceRoles.Spine;
            }

            list.Add(new ReadSource(
                domain!,
                ReadString(entry, "selector") ?? string.Empty,
                ReadInt(entry, "precedence") ?? index,
                ReadBool(entry, "anchorCapable"),
                ReadString(entry, "surface"),
                facts,
                role));
            index++;
        }

        return list;
    }

    // ---------- answer-facts.json ----------

    private static AnswerRule ParseAnswerRule(string json, string terminalPresentation, List<string> errors)
    {
        if (!TryParse(json, "answer-facts", errors, out var root)) return AnswerRule.Empty;

        var authority = ReadStringArray(root, "authority");
        if (authority.Count == 0)
        {
            errors.Add("[A1] answer-facts.authority 为空");
        }

        var eligible = new List<string>();
        var select = new List<string>();
        if (root.TryGetProperty("domains", out var domains) && domains.ValueKind == JsonValueKind.Array)
        {
            foreach (var domain in domains.EnumerateArray())
            {
                if (domain.ValueKind != JsonValueKind.Object) continue;
                if (!ReadBool(domain, "enabled", defaultValue: true)) continue;
                eligible.AddRange(ReadStringArray(domain, "eligible"));
                select.AddRange(ReadStringArray(domain, "select"));
            }
        }

        if (eligible.Count == 0) errors.Add("[A1] answer-facts.domains 未声明 eligible");
        if (select.Count == 0) errors.Add("[A1] answer-facts.domains 未声明 select");

        return new AnswerRule(
            authority,
            eligible.Distinct(StringComparer.Ordinal).ToArray(),
            select.Distinct(StringComparer.Ordinal).ToArray(),
            AnswerStatus.NoAnswer,
            terminalPresentation);
    }

    // ---------- ui/fold.json（折叠受控词单源） ----------

    /// <summary>折叠词表：来自声明 <c>ui/fold.json</c> 的 <c>modes</c> 键；缺省 ⇒ 兼容词表。
    /// 词表**不在编译器硬编码第二份**（否则声明新增词即编译失败——本次 history/round-closed 即此故障）。</summary>
    private static IReadOnlyList<string> ParseFoldWords(string? json, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(json)) return DefaultFoldWords;
        if (!TryParse(json, "fold", errors, out var root)) return DefaultFoldWords;
        if (!root.TryGetProperty("modes", out var modes) || modes.ValueKind != JsonValueKind.Object)
        {
            errors.Add("[V2] fold.modes 缺失或非对象");
            return DefaultFoldWords;
        }

        var words = modes.EnumerateObject()
            .Select(static p => p.Name)
            .Where(static name => !name.StartsWith("$", StringComparison.Ordinal))
            .ToArray();
        if (words.Length == 0)
        {
            errors.Add("[V2] fold.modes 为空");
            return DefaultFoldWords;
        }

        return words;
    }

    // ---------- ui-regions.json + gather/conversation.json ----------

    private static ViewPlanTable ParseViews(
        string uiJson, string gatherJson, IReadOnlyList<string> foldWords, List<string> errors)
    {
        var result = new Dictionary<string, ViewPlanRow>(StringComparer.Ordinal);
        if (!TryParse(uiJson, "ui-regions", errors, out var root)) return ViewPlanTable.Empty;
        if (!root.TryGetProperty("views", out var views) || views.ValueKind != JsonValueKind.Array)
        {
            errors.Add("[V1] ui-regions.views 缺失或非数组");
            return ViewPlanTable.Empty;
        }

        JsonElement? gatherRoot = null;
        if (TryParse(gatherJson, "gather", null, out var gather)) gatherRoot = gather;

        foreach (var entry in views.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var view = ReadString(entry, "view");
            if (string.IsNullOrWhiteSpace(view))
            {
                errors.Add("[V1] ui-regions 视图缺 view 键");
                continue;
            }

            if (result.ContainsKey(view!))
            {
                errors.Add($"[V1] ui-regions 视图重复：{view}");
                continue;
            }

            var contentDomain = string.Empty;
            var contentScope = string.Empty;
            var contentFold = "none";
            if (entry.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object)
            {
                contentDomain = ReadString(content, "domain") ?? string.Empty;
                contentScope = ReadString(content, "scope") ?? string.Empty;
                contentFold = ReadString(content, "fold") ?? "none";
            }

            if (string.IsNullOrWhiteSpace(contentDomain))
            {
                errors.Add($"[V2] 视图 {view} 缺 content.domain");
            }

            if (!foldWords.Contains(contentFold, StringComparer.Ordinal))
            {
                errors.Add($"[V2] 视图 {view} 的 content.fold '{contentFold}' 不在受控词（{string.Join('/', foldWords)}）");
            }

            var gatherView = ReadString(entry, "gatherView") ?? view;
            var stages = ResolveStages(gatherRoot, gatherView, view!, errors);

            result[view!] = new ViewPlanRow(
                view!,
                ReadString(entry, "typeKey") ?? view!,
                contentDomain,
                contentScope,
                contentFold,
                ReadStringArray(entry, "regions"),
                stages);
        }

        return new ViewPlanTable(result, string.Empty);
    }

    private static IReadOnlyList<ViewStage> ResolveStages(
        JsonElement? gatherRoot, string? gatherView, string view, List<string> errors)
    {
        if (gatherRoot is not { } root || string.IsNullOrWhiteSpace(gatherView)) return Array.Empty<ViewStage>();
        if (!root.TryGetProperty("views", out var views) || views.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"[V3] gather 声明缺 views（视图 {view} 的 stages 无法解析）");
            return Array.Empty<ViewStage>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = gatherView;
        while (!string.IsNullOrWhiteSpace(current) && seen.Add(current!))
        {
            if (!views.TryGetProperty(current!, out var spec) || spec.ValueKind != JsonValueKind.Object) break;
            if (spec.TryGetProperty("stages", out var stages) && stages.ValueKind == JsonValueKind.Array)
            {
                return stages.EnumerateArray()
                    .Where(static s => s.ValueKind == JsonValueKind.Object)
                    .Select(static s => new ViewStage(
                        ReadString(s, "name") ?? string.Empty,
                        ReadStringArray(s, "needs"),
                        ReadStringArray(s, "deps"),
                        ReadStringArray(s, "sections")))
                    .ToArray();
            }

            current = ReadString(spec, "extends");
        }

        errors.Add($"[V3] gather 视图 {gatherView} 无 stages 且无 extends 可解析（视图 {view}）");
        return Array.Empty<ViewStage>();
    }

    // ---------- 交叉校验 ----------

    private static void ValidateCross(
        FrameTable frames,
        IReadOnlyList<string> presentations,
        IReadOnlyList<ReadSource> sources,
        ViewPlanTable views,
        List<string> errors)
    {
        var presentationSet = new HashSet<string>(presentations, StringComparer.Ordinal);
        var viewSet = new HashSet<string>(views.Views.Keys, StringComparer.Ordinal);

        foreach (var row in frames.Rows.Values)
        {
            if (presentations.Count > 0 && !presentationSet.Contains(row.Presentation))
            {
                errors.Add($"[F3] 帧 {row.Kind} 的 presentation '{row.Presentation}' 未在 presentation.classes 声明");
            }

            foreach (var declaredView in row.Views)
            {
                if (!viewSet.Contains(declaredView))
                {
                    errors.Add($"[F9] 帧 {row.Kind} 声明出现于视图 '{declaredView}'，但 ui-regions.views 未声明该视图");
                }
            }
        }

        if (views.Views.Count == 0) errors.Add("[V1] 无任何视图计划（ui-regions.views 为空）");
        if (sources.Count == 0) errors.Add("[R1] 无任何读源（read-paths.paths 为空）");

        var declaredDomains = views.Views.Values
            .Select(static v => v.ContentDomain)
            .Where(static d => !string.IsNullOrWhiteSpace(d))
            .ToArray();
        foreach (var group in declaredDomains.GroupBy(static d => d, StringComparer.Ordinal).Where(static g => g.Count() > 1))
        {
            errors.Add($"[V2] 视图内容域重复（须两两不同）：'{group.Key}' 出现 {group.Count()} 次");
        }
    }

    // ---------- ReadPlan 派生 ----------

    // ---------- read-paths.json：按计划绑定源 ----------

    private sealed record PlanBinding(IReadOnlyList<string> Spine, IReadOnlyList<string> Supplement);

    private static IReadOnlyDictionary<string, PlanBinding> ParsePlanBindings(string json, List<string> errors)
    {
        var result = new Dictionary<string, PlanBinding>(StringComparer.Ordinal);
        if (!TryParse(json, "read-paths", errors, out var root)) return result;
        if (!root.TryGetProperty("plans", out var plans) || plans.ValueKind != JsonValueKind.Object) return result;

        foreach (var entry in plans.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"[R2] read-paths.plans.{entry.Name} 非对象");
                continue;
            }

            var spine = ReadStringArray(entry.Value, "spine");
            var supplement = ReadStringArray(entry.Value, "supplement");
            if (spine.Count == 0 && supplement.Count == 0)
            {
                errors.Add($"[R2] read-paths.plans.{entry.Name} 未声明 spine/supplement");
                continue;
            }

            result[entry.Name] = new PlanBinding(spine, supplement);
        }

        return result;
    }

    private static ReadPlanTable BuildReadPlans(
        ViewPlanTable views,
        IReadOnlyList<ReadSource> sources,
        AnswerRule answer,
        IReadOnlyDictionary<string, PlanBinding> bindings,
        List<string> errors)
    {
        var byDomain = new Dictionary<string, ReadSource>(StringComparer.Ordinal);
        foreach (var source in sources) byDomain.TryAdd(source.Domain, source);

        IReadOnlyList<ReadSource> All() => sources.OrderBy(static s => s.Precedence).ToArray();

        IReadOnlyList<ReadSource> Select(string key, PlanBinding binding)
        {
            var picked = new List<ReadSource>(binding.Spine.Count + binding.Supplement.Count);
            Add(binding.Spine, ReadSourceRoles.Spine);
            Add(binding.Supplement, ReadSourceRoles.Supplement);
            return picked.OrderBy(static s => s.Precedence).ToArray();

            void Add(IReadOnlyList<string> domains, string expectedRole)
            {
                foreach (var domain in domains)
                {
                    if (!byDomain.TryGetValue(domain, out var source))
                    {
                        errors.Add($"[R2] read-paths.plans.{key} 引用未声明域 '{domain}'");
                        continue;
                    }

                    if (!string.Equals(source.Role, expectedRole, StringComparison.Ordinal))
                    {
                        errors.Add($"[R2] read-paths.plans.{key} 把域 '{domain}' 列入 {expectedRole}，但其 paths[].role='{source.Role}'（两处声明须一致）");
                    }

                    picked.Add(source);
                }
            }
        }

        var plans = new Dictionary<string, ReadPlan>(StringComparer.Ordinal);
        foreach (var view in views.Views.Keys)
        {
            plans[view] = new ReadPlan(
                view,
                bindings.TryGetValue(view, out var binding) ? Select(view, binding) : All(),
                answer,
                string.Empty);
        }

        // 视图之外的读计划（如 LLM 会话脊梁 session）同样来自声明，禁在 C# 另写源集。
        foreach (var pair in bindings)
        {
            if (plans.ContainsKey(pair.Key)) continue;
            plans[pair.Key] = new ReadPlan(pair.Key, Select(pair.Key, pair.Value), answer, string.Empty);
        }

        return new ReadPlanTable(plans, string.Empty);
    }

    // ---------- 指纹 ----------

    private static string Fingerprint(FrameTable frames, ReadPlanTable reads, ViewPlanTable views)
    {
        var builder = new StringBuilder(Schema);
        foreach (var row in frames.Rows.Values.OrderBy(static r => r.Kind, StringComparer.Ordinal))
        {
            builder.Append("\nF|").Append(row.Kind).Append('|').Append(row.Class).Append('|')
                .Append(row.Presentation).Append('|').Append((int)row.Anchor).Append('|')
                .Append(row.Archive ? 1 : 0).Append('|').Append(string.Join(',', row.Views)).Append('|')
                .Append(row.Fold).Append('|').Append(row.Ordinal).Append('|').Append(row.Subject);
        }

        foreach (var plan in reads.Plans.Values.OrderBy(static p => p.View, StringComparer.Ordinal))
        {
            builder.Append("\nR|").Append(plan.View).Append('|')
                .Append(string.Join(',', plan.Answer.Authority)).Append('|')
                .Append(string.Join(',', plan.Answer.Eligible)).Append('|')
                .Append(string.Join(',', plan.Answer.Select)).Append('|')
                .Append((int)plan.Answer.Absent).Append('|').Append(plan.Answer.TerminalPresentation);
            foreach (var source in plan.Sources)
            {
                builder.Append("\nS|").Append(source.Domain).Append('|').Append(source.Selector).Append('|')
                    .Append(source.Precedence).Append('|').Append(source.AnchorCapable ? 1 : 0).Append('|')
                    .Append(source.Surface ?? string.Empty).Append('|').Append(source.Role).Append('|')
                    .Append(string.Join(',', source.Facts));
            }
        }

        foreach (var view in views.Views.Values.OrderBy(static v => v.View, StringComparer.Ordinal))
        {
            builder.Append("\nV|").Append(view.View).Append('|').Append(view.TypeKey).Append('|')
                .Append(view.ContentDomain).Append('|').Append(view.ContentScope).Append('|')
                .Append(view.Fold).Append('|').Append(string.Join(',', view.Regions));
            foreach (var stage in view.Stages)
            {
                builder.Append("\nG|").Append(stage.Name).Append('|')
                    .Append(string.Join(',', stage.Needs)).Append('|')
                    .Append(string.Join(',', stage.Deps)).Append('|')
                    .Append(string.Join(',', stage.Sections));
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    // ---------- JSON 原语 ----------

    private static bool TryParse(string json, string label, List<string>? errors, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            errors?.Add($"[{label}] 声明文本为空");
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
            // fail-closed：根必须是 JSON 对象；否则必须记 error，禁「返回 false 但无诊断」的静默空表。
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors?.Add($"[{label}] 声明根必须为 JSON 对象（实际 {root.ValueKind}）");
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            errors?.Add($"[{label}] 声明非法 JSON：{ex.Message}");
            return false;
        }
    }

    private static AnchorPolicy AddError(List<string> errors, string message)
    {
        errors.Add(message);
        return AnchorPolicy.Never;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var node)
           && node.ValueKind == JsonValueKind.String
           && node.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static bool ReadBool(JsonElement element, string name, bool defaultValue = false)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var node)
           && node.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? node.GetBoolean()
            : defaultValue;

    private static int? ReadInt(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var node)
           && node.ValueKind == JsonValueKind.Number
           && node.TryGetInt32(out var value)
            ? value
            : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var node)
            || node.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return node.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 })
            .Select(static item => item.GetString()!)
            .ToArray();
    }
}
