#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Modules.Gather;

/// <summary>
/// 聚拢声明装载器（**无降级**）：解析 → 结构校验 → 装配期合格性判定。
/// <para>
/// 违反 W-4「缺失即抛」一律抛 <see cref="InvalidOperationException"/>（不返回兜底规格）；
/// 调用方如需要回退（如迁移期 legacy 常量），必须自行显式回退并打可见化日志。
/// </para>
/// </summary>
public static class GatherSpecLoader
{
    /// <summary>受支持的声明 schema（单一权威）。</summary>
    public const string SupportedSchema = "gather-spec-v1";

    /// <summary>声明面禁用的过程语义键（对齐 <c>SEP.DeclarativeProcessDisjoint</c>）。</summary>
    private static readonly string[] ForbiddenKeys = { "step", "order", "sequence", "then", "next" };

    /// <summary>读取 + 校验聚拢声明；任何不合格 ⇒ 抛。</summary>
    public static GatherSpecSet Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"聚拢声明缺失: {path}（缺失即抛，禁回退 C# 常量）");
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"聚拢声明读取失败: {path}（{ex.Message}）", ex);
        }

        return LoadFromText(text, path);
    }

    /// <summary>由声明文本装载（唯一解析实现；<paramref name="origin"/> 仅用于诊断文案）。</summary>
    public static GatherSpecSet LoadFromText(string text, string origin)
    {
        ArgumentNullException.ThrowIfNull(text);
        var path = string.IsNullOrWhiteSpace(origin) ? "(inline)" : origin;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"聚拢声明解析失败: {path}（{ex.Message}）", ex);
        }

        if (root is not JsonObject rootObject)
        {
            throw new InvalidOperationException($"聚拢声明根须为对象: {path}");
        }

        var forbidden = FindForbiddenKeys(rootObject, "$");
        if (forbidden.Count > 0)
        {
            throw new InvalidOperationException(
                $"聚拢声明含过程语义键（禁 step/order/sequence/then/next）: {string.Join(", ", forbidden)}");
        }

        var schema = ReadText(rootObject["schema"]) ?? string.Empty;
        if (!string.Equals(schema, SupportedSchema, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"聚拢声明 schema 非法: '{schema}'（期望 {SupportedSchema}）");
        }

        if (rootObject["views"] is not JsonObject viewsNode || viewsNode.Count == 0)
        {
            throw new InvalidOperationException("聚拢声明缺 views（须为非空对象）");
        }

        var views = new Dictionary<string, GatherViewSpec>(StringComparer.Ordinal);
        foreach (var pair in viewsNode)
        {
            if (pair.Value is not JsonObject viewNode)
            {
                throw new InvalidOperationException($"视图 '{pair.Key}' 声明须为对象");
            }

            views[pair.Key] = ReadView(pair.Key, viewNode);
        }

        // 组合折叠先于合格性判定：raw 视图（只含 extends）不满足 V3「无 stages」，
        // 直接校验会把合法组合声明误判为红。
        var inheritanceIssues = new List<string>();
        var effectiveViews = ResolveInheritance(views, inheritanceIssues);
        if (inheritanceIssues.Count > 0)
        {
            throw new InvalidOperationException(
                $"聚拢声明组合不合格（{path}）：{string.Join("；", inheritanceIssues)}");
        }

        var spec = new GatherSpecSet(schema, effectiveViews);
        var issues = Validate(spec);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException($"聚拢声明不合格（{path}）：{string.Join("；", issues)}");
        }

        return spec;
    }

    /// <summary>尝试装载（失败 ⇒ <c>false</c> + <c>null</c>；调用方须可见化）。</summary>
    public static bool TryLoad(string path, out GatherSpecSet? spec)
    {
        try
        {
            spec = Load(path);
            return true;
        }
        catch (Exception)
        {
            spec = null;
            return false;
        }
    }

    /// <summary>装配期合格性判定（纯函数；门禁 <c>Check-GatherSpec</c> 复用同一口径）。</summary>
    public static IReadOnlyList<string> Validate(GatherSpecSet spec)
    {
        var issues = new List<string>();
        if (spec.Views.Count == 0)
        {
            issues.Add("[V1] views 为空（判据无标的）");
        }

        foreach (var pair in spec.Views)
        {
            var view = pair.Value;
            if (!string.Equals(view.View, pair.Key, StringComparison.Ordinal))
            {
                issues.Add($"[V2] 视图键 '{pair.Key}' 与声明内 view='{view.View}' 不一致");
            }

            // 命名判据（单源 = GatherSchema.IsValidName）：视图声明数据面与实现侧声明同规，
            // 否则「声明与实现同名同错」会同时通过 R9/R10 对账，只在运行期降级。
            if (!GatherSchema.IsValidName(pair.Key))
            {
                issues.Add($"[V11] 视图名非法：'{pair.Key}'（须匹配 {GatherSchema.NamePattern}）");
            }

            if (view.Stages.Count == 0)
            {
                issues.Add($"[V3] 视图 '{pair.Key}' 无 stages");
            }

            var needDomains = new HashSet<string>(StringComparer.Ordinal);
            foreach (var need in view.Needs)
            {
                if (string.IsNullOrWhiteSpace(need.Domain) || string.IsNullOrWhiteSpace(need.Scope))
                {
                    issues.Add($"[V4] 视图 '{pair.Key}' 存在缺 domain/scope 的 need");
                }

                if (!needDomains.Add(need.Domain))
                {
                    issues.Add($"[V4] 视图 '{pair.Key}' need 域重复：'{need.Domain}'");
                }

                if (!GatherSchema.IsValidName(need.Domain))
                {
                    issues.Add($"[V11] 视图 '{pair.Key}' need 域名非法：'{need.Domain}'（须匹配 {GatherSchema.NamePattern}）");
                }
            }

            var stageNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var stage in view.Stages)
            {
                if (string.IsNullOrWhiteSpace(stage.Name))
                {
                    issues.Add($"[V5] 视图 '{pair.Key}' 存在无名 stage");
                    continue;
                }

                if (!stageNames.Add(stage.Name))
                {
                    issues.Add($"[V5] 视图 '{pair.Key}' stage 重名：'{stage.Name}'");
                }

                if (!GatherSchema.IsValidName(stage.Name))
                {
                    issues.Add($"[V11] 视图 '{pair.Key}' stage 名非法：'{stage.Name}'（须匹配 {GatherSchema.NamePattern}）");
                }

                foreach (var needDomain in stage.Needs)
                {
                    if (!needDomains.Contains(needDomain))
                    {
                        issues.Add($"[V6] 视图 '{pair.Key}' 阶段 '{stage.Name}' 引用未声明 need '{needDomain}'");
                    }
                }
            }

            foreach (var stage in view.Stages)
            {
                foreach (var dep in stage.Deps)
                {
                    if (!stageNames.Contains(dep))
                    {
                        issues.Add($"[V7] 视图 '{pair.Key}' 阶段 '{stage.Name}' 依赖不存在的阶段 '{dep}'");
                    }
                }
            }

            if (HasCycle(view.Stages))
            {
                issues.Add($"[V8] 视图 '{pair.Key}' 的 deps 存在环（拓扑不可解）");
            }

            var produceNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var produce in view.Produces)
            {
                if (string.IsNullOrWhiteSpace(produce.Name))
                {
                    issues.Add($"[V9] 视图 '{pair.Key}' 存在无名 produce");
                    continue;
                }

                if (!produceNames.Add(produce.Name))
                {
                    issues.Add($"[V9] 视图 '{pair.Key}' produce 重名：'{produce.Name}'");
                }

                if (!GatherSchema.IsValidName(produce.Name))
                {
                    issues.Add($"[V11] 视图 '{pair.Key}' produce 名非法：'{produce.Name}'（须匹配 {GatherSchema.NamePattern}）");
                }

                if (produce.Crit is not ("micro" or "macro"))
                {
                    issues.Add($"[V10] 视图 '{pair.Key}' 产物 '{produce.Name}' 的 crit='{produce.Crit}' 非法（∈ micro|macro）");
                }

                if (produce.Crit == "macro" && string.IsNullOrWhiteSpace(produce.Recompute))
                {
                    issues.Add($"[V10] 视图 '{pair.Key}' 产物 '{produce.Name}' 为 crit:macro 但缺 recompute");
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// 组合折叠（唯一实现）：<c>extends</c> 链 → 合并（stage 按 name / need 按 domain / produce 按 name 覆盖）
    /// → 应用 <c>exclude*</c> → 输出 effective 视图（不含组合意图）。
    /// <para>判据：V12 extends 链无环；V13 基视图与排除项均须已声明（排除不存在的项 = 声明漂移）。</para>
    /// </summary>
    public static IReadOnlyDictionary<string, GatherViewSpec> ResolveInheritance(
        IReadOnlyDictionary<string, GatherViewSpec> raw,
        List<string> issues)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(issues);

        var resolved = new Dictionary<string, GatherViewSpec>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        GatherViewSpec? ResolveOne(string key, out bool failed)
        {
            failed = false;
            if (resolved.TryGetValue(key, out var done))
            {
                return done;
            }

            if (!raw.TryGetValue(key, out var view))
            {
                failed = true;
                issues.Add($"[V13] 视图 '{key}' 未声明");
                return null;
            }

            if (!visiting.Add(key))
            {
                failed = true;
                issues.Add($"[V12] 视图 '{key}' 的 extends 链存在环");
                return null;
            }

            GatherViewSpec effective = view;
            if (!string.IsNullOrWhiteSpace(view.Extends))
            {
                var parentKey = view.Extends!.Trim();
                if (!GatherSchema.IsValidName(parentKey))
                {
                    failed = true;
                    issues.Add($"[V15] 视图 '{key}' 的 extends 名非法：'{parentKey}'（须匹配 {GatherSchema.NamePattern}）");
                }
                else
                {
                    var parent = ResolveOne(parentKey, out var parentFailed);
                    if (parentFailed || parent is null)
                    {
                        failed = true;
                    }
                    else
                    {
                        effective = Merge(parent, view, key, issues);
                    }
                }
            }

            visiting.Remove(key);
            if (failed)
            {
                return null;
            }

            resolved[key] = effective;
            return effective;
        }

        var all = new Dictionary<string, GatherViewSpec>(StringComparer.Ordinal);
        foreach (var key in raw.Keys)
        {
            var effective = ResolveOne(key, out _);
            if (effective is not null)
            {
                all[key] = effective;
            }
        }

        return all;
    }

    private static GatherViewSpec Merge(
        GatherViewSpec parent, GatherViewSpec child, string key, List<string> issues)
    {
        var excludeStages = new HashSet<string>(child.ExcludeStages ?? Array.Empty<string>(), StringComparer.Ordinal);
        var excludeNeeds = new HashSet<string>(child.ExcludeNeeds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var excludeProduces = new HashSet<string>(child.ExcludeProduces ?? Array.Empty<string>(), StringComparer.Ordinal);

        var stages = new List<GatherStageSpec>(parent.Stages);
        var stageIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < stages.Count; i++)
        {
            stageIndex[stages[i].Name] = i;
        }

        foreach (var name in excludeStages)
        {
            if (!stageIndex.ContainsKey(name))
            {
                issues.Add($"[V13] 视图 '{key}' excludeStages 指向基视图不存在的阶段 '{name}'");
            }
        }

        foreach (var stage in child.Stages)
        {
            if (stageIndex.TryGetValue(stage.Name, out var index))
            {
                stages[index] = stage;
            }
            else
            {
                stageIndex[stage.Name] = stages.Count;
                stages.Add(stage);
            }
        }

        var keptStages = stages.Where(stage => !excludeStages.Contains(stage.Name)).ToList();

        var needs = new List<GatherNeedSpec>(parent.Needs);
        var needIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < needs.Count; i++)
        {
            needIndex[needs[i].Domain] = i;
        }

        foreach (var domain in excludeNeeds)
        {
            if (!needIndex.ContainsKey(domain))
            {
                issues.Add($"[V13] 视图 '{key}' excludeNeeds 指向基视图不存在的 need '{domain}'");
            }
        }

        foreach (var need in child.Needs)
        {
            if (needIndex.TryGetValue(need.Domain, out var index))
            {
                needs[index] = need;
            }
            else
            {
                needIndex[need.Domain] = needs.Count;
                needs.Add(need);
            }
        }

        var keptNeeds = needs.Where(need => !excludeNeeds.Contains(need.Domain)).ToList();

        var produces = new List<GatherProduceSpec>(parent.Produces);
        var produceIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < produces.Count; i++)
        {
            produceIndex[produces[i].Name] = i;
        }

        foreach (var name in excludeProduces)
        {
            if (!produceIndex.ContainsKey(name))
            {
                issues.Add($"[V13] 视图 '{key}' excludeProduces 指向基视图不存在的产物 '{name}'");
            }
        }

        foreach (var produce in child.Produces)
        {
            if (produceIndex.TryGetValue(produce.Name, out var index))
            {
                produces[index] = produce;
            }
            else
            {
                produceIndex[produce.Name] = produces.Count;
                produces.Add(produce);
            }
        }

        var keptProduces = produces.Where(produce => !excludeProduces.Contains(produce.Name)).ToList();

        return new GatherViewSpec(child.View, keptStages, keptNeeds, keptProduces);
    }

    private static GatherViewSpec ReadView(string key, JsonObject node)
    {
        var stages = new List<GatherStageSpec>();
        if (node["stages"] is JsonArray stagesNode)
        {
            foreach (var entry in stagesNode)
            {
                if (entry is not JsonObject stageNode)
                {
                    continue;
                }

                stages.Add(new GatherStageSpec(
                    ReadText(stageNode["name"]) ?? string.Empty,
                    ReadTextArray(stageNode["needs"]),
                    ReadTextArray(stageNode["deps"]),
                    ReadTextArray(stageNode["sections"])));
            }
        }

        var needs = new List<GatherNeedSpec>();
        if (node["needs"] is JsonArray needsNode)
        {
            foreach (var entry in needsNode)
            {
                if (entry is not JsonObject needNode)
                {
                    continue;
                }

                needs.Add(new GatherNeedSpec(
                    ReadText(needNode["domain"]) ?? string.Empty,
                    ReadText(needNode["scope"]) ?? string.Empty,
                    ReadCost(needNode["cost"]),
                    ReadBool(needNode["reusable"])));
            }
        }

        var produces = new List<GatherProduceSpec>();
        if (node["produces"] is JsonArray producesNode)
        {
            foreach (var entry in producesNode)
            {
                if (entry is not JsonObject produceNode)
                {
                    continue;
                }

                produces.Add(new GatherProduceSpec(
                    ReadText(produceNode["name"]) ?? string.Empty,
                    ReadText(produceNode["kind"]) ?? "dom-region",
                    ReadText(produceNode["retention"]) ?? "contextual",
                    ReadText(produceNode["crit"]) ?? "micro",
                    ReadText(produceNode["recompute"])));
            }
        }

        return new GatherViewSpec(
            key, stages, needs, produces,
            Extends: ReadText(node["extends"]),
            ExcludeStages: ReadTextArray(node["excludeStages"]),
            ExcludeNeeds: ReadTextArray(node["excludeNeeds"]),
            ExcludeProduces: ReadTextArray(node["excludeProduces"]));
    }

    private static bool HasCycle(IReadOnlyList<GatherStageSpec> stages)
    {
        var byName = new Dictionary<string, GatherStageSpec>(StringComparer.Ordinal);
        foreach (var stage in stages)
        {
            byName[stage.Name] = stage;
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);

        bool Visit(string name)
        {
            if (done.Contains(name))
            {
                return false;
            }

            if (!visiting.Add(name))
            {
                return true;
            }

            if (byName.TryGetValue(name, out var stage))
            {
                foreach (var dep in stage.Deps)
                {
                    if (Visit(dep))
                    {
                        return true;
                    }
                }
            }

            visiting.Remove(name);
            done.Add(name);
            return false;
        }

        foreach (var stage in stages)
        {
            if (Visit(stage.Name))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> FindForbiddenKeys(JsonNode node, string path)
    {
        var hits = new List<string>();
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    var childPath = $"{path}.{pair.Key}";
                    foreach (var key in ForbiddenKeys)
                    {
                        if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                        {
                            hits.Add(childPath);
                        }
                    }

                    if (pair.Value is not null)
                    {
                        hits.AddRange(FindForbiddenKeys(pair.Value, childPath));
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is not null)
                    {
                        hits.AddRange(FindForbiddenKeys(array[i]!, $"{path}[{i}]"));
                    }
                }

                break;
        }

        return hits;
    }

    private static string? ReadText(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static IReadOnlyList<string> ReadTextArray(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(array.Count);
        foreach (var entry in array)
        {
            var text = ReadText(entry);
            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(text!);
            }
        }

        return result;
    }

    private static GatherCostClass ReadCost(JsonNode? node)
    {
        var text = ReadText(node);
        return string.Equals(text, "high", StringComparison.OrdinalIgnoreCase)
            ? GatherCostClass.High
            : GatherCostClass.Low;
    }

    private static bool ReadBool(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}
