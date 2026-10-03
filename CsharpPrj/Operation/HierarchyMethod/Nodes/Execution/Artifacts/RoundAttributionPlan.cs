using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 入史计划（轮末沉淀的**中立载荷契约**）—— <c>project_marked</c> 的入参形状：
/// 入口端口（Agent 侧）负责「哪些节点入史 + 标记 + 序号 + 轮次」，fn 单步体只负责「物化 + 挂载」，
/// 两侧经本契约对齐，避免 Agent.Core 与 Agent.Modules 之间的形状重复（分层无环）。
/// <para>轮次随计划携带（而非独立 inport）：避免为「round」新增会话种子键契约（键面最窄化）。</para>
/// </summary>
/// <param name="Round">轮次号（≤0 ⇒ 消费方归一为 1，防跨轮同名覆盖）。</param>
/// <param name="Messages">入史条目（按产出顺序编号）。</param>
public sealed record PlannedRoundAttribution(int Round, IReadOnlyList<PlannedRoundMessage> Messages);

/// <summary>
/// 入史条目（节点 → 会话消息的落点描述）。
/// </summary>
/// <param name="NodeKey">节点键（会话消息节点身份来源）。</param>
/// <param name="Ordinal">轮内**全局到达序**（1 起；= 本轮节点产出源索引，**非**入史条目计数）——键身份组成。</param>
/// <param name="Kind">内容类别（键身份组成；取词表值，由调用方经 <c>kindOfMark</c> 注入，Frame 层不持词表）。</param>
/// <param name="Text">消息文本（终答节点 = 轮终答文本；其余 = 首个非空白端口文本；可为空串）。</param>
/// <param name="Mark">入史标记名（<see cref="NodeHistoryMark"/> 具名值）。</param>
/// <param name="AtUnixMs">产出时刻（Unix 毫秒；0 = 缺失，由消费方取当前时刻）。</param>
public sealed record PlannedRoundMessage(
    string NodeKey, long Ordinal, string Kind, string Text, string Mark, long AtUnixMs);

/// <summary>
/// 轮末入史计划（DEBT-4 单源）—— 把「标记判定 + 文本提取 + 序号编排 + 轮次归一」收敛为一处纯函数：
/// <see cref="Build"/> 复用 <see cref="HistoryMarkPolicy"/>（标记唯一权威）与
/// <see cref="RoundArtifacts.TerminalText"/>（终答文本），<see cref="ToJson"/>/<see cref="FromJson"/>
/// 为跨程序集（Agent.Core ⇄ Agent.Modules）的载荷编解码。
/// </summary>
public static class RoundAttributionPlan
{
    /// <summary>
    /// 装配入史计划：<paramref name="ordinal"/> = **本轮节点产出源索引**（1 起，含未入史节点占位），
    /// 使键内序数与「轮内全局到达序」同义（W2-A2 裁决）；仅纳入
    /// <see cref="HistoryMarkPolicy"/> 判定为入史的节点。
    /// </summary>
    /// <param name="artifacts">轮次产物（含终答归因与文本）。</param>
    /// <param name="spec">本轮骨架（提供声明面标记；null = 仅默认规则）。</param>
    /// <param name="kindOfMark">
    /// 标记名 → 内容类别 的映射（**必填**）：类别 ∈ 识别词表，帧侧不得自持词表字面量
    /// （L0/L1 分层：词表单源在 Agent 侧 <c>RecognizedKinds</c>）。
    /// </param>
    public static PlannedRoundAttribution Build(
        RoundArtifacts artifacts, RoundSpec? spec, Func<string?, string> kindOfMark)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(kindOfMark);
        var nodes = artifacts.Nodes ?? (IReadOnlyList<NodeExecutionRecord>)Array.Empty<NodeExecutionRecord>();
        var messages = new List<PlannedRoundMessage>();

        for (var index = 0; index < nodes.Count; index++)
        {
            var record = nodes[index];
            var mark = HistoryMarkPolicy.Resolve(spec, record, artifacts.TerminalNodeKey);
            if (mark == NodeHistoryMark.None) continue;

            var isTerminal = mark == NodeHistoryMark.Terminal;
            var text = isTerminal && !string.IsNullOrWhiteSpace(artifacts.TerminalText)
                ? artifacts.TerminalText!
                : RoundMessageProjection.FirstPortText(record.OutputPorts) ?? string.Empty;

            messages.Add(new PlannedRoundMessage(
                record.NodeKey,
                RoundArtifactAssembler.ArrivalOrdinal(index),
                kindOfMark(mark.ToString()),
                text,
                mark.ToString(),
                record.AtUtc == default ? 0 : record.AtUtc.ToUnixTimeMilliseconds()));
        }

        // 轮次归一（键的身份组成）：0/负值会使「跨轮同名覆盖」回归，故下限取 1。
        return new PlannedRoundAttribution(artifacts.Round > 0 ? artifacts.Round : 1, messages);
    }

    /// <summary>入史计划 → JSON 文本（fn 入参；空计划 → <c>{"round":1,"messages":[]}</c>）。</summary>
    /// <param name="plan">入史计划。</param>
    public static string ToJson(PlannedRoundAttribution plan)
    {
        var array = new JsonArray();
        foreach (var item in plan.Messages)
        {
            array.Add(new JsonObject
            {
                ["node_key"] = item.NodeKey,
                ["ordinal"] = item.Ordinal,
                ["kind"] = item.Kind,
                ["text"] = item.Text,
                ["mark"] = item.Mark,
                ["at_unix_ms"] = item.AtUnixMs,
            });
        }

        return new JsonObject
        {
            ["round"] = plan.Round,
            ["messages"] = array,
        }.ToJsonString();
    }

    /// <summary>JSON 文本 → 入史计划（空 / 非法 → 空计划，不抛）。</summary>
    /// <param name="json">JSON 文本（fn 入参值）。</param>
    public static PlannedRoundAttribution FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new PlannedRoundAttribution(1, Array.Empty<PlannedRoundMessage>());
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                return new PlannedRoundAttribution(1, Array.Empty<PlannedRoundMessage>());

            var roundText = root["round"]?.ToString();
            var round = int.TryParse(roundText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRound)
                ? parsedRound
                : 1;

            var messages = new List<PlannedRoundMessage>();
            if (root["messages"] is JsonArray array)
            {
                foreach (var item in array)
                {
                    var node = item as JsonObject;
                    var nodeKey = node?["node_key"]?.ToString();
                    if (string.IsNullOrWhiteSpace(nodeKey)) continue;

                    // 序数：新形态读 ordinal；存量载荷（seq）只读兼容（同一「轮内到达序」语义，不伪造类别）。
                    var ordinalText = node?["ordinal"]?.ToString() ?? node?["seq"]?.ToString();
                    var ordinal = long.TryParse(ordinalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOrdinal) ? parsedOrdinal : 0L;
                    var atText = node?["at_unix_ms"]?.ToString();
                    var at = long.TryParse(atText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) ? ms : 0L;

                    messages.Add(new PlannedRoundMessage(
                        nodeKey!,
                        ordinal,
                        node?["kind"]?.ToString() ?? string.Empty,
                        node?["text"]?.ToString() ?? string.Empty,
                        node?["mark"]?.ToString() ?? string.Empty,
                        at));
                }
            }

            return new PlannedRoundAttribution(round, messages);
        }
        catch (JsonException)
        {
            return new PlannedRoundAttribution(1, Array.Empty<PlannedRoundMessage>());
        }
    }
}

