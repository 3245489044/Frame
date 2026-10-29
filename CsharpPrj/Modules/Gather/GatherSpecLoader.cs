#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
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

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path));
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

        var spec = new GatherSpecSet(schema, views);
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

        return new GatherViewSpec(key, stages, needs, produces);
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
