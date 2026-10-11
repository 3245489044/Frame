using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Modules.Node.Stream;

/// <summary>一条运行中的**聚合帧**（尚未落库）：到达序由 <see cref="Seq"/> 表达（数组序 = 到达序）。</summary>
/// <param name="Seq">到达序（1..N；容器级、跨轮不重置）。</param>
/// <param name="Kind">帧类别。</param>
/// <param name="Round">所属轮次（帧数组的分组轴）。</param>
/// <param name="PayloadJson">帧载荷 JSON。</param>
/// <param name="AtUtc">事件到达时刻。</param>
public sealed record AggregateFrame(long Seq, string Kind, int Round, string PayloadJson, DateTimeOffset AtUtc);

/// <summary>一轮内的聚合帧序列（保序；数组序 = 到达序）。</summary>
/// <param name="Round">轮次号。</param>
/// <param name="Frames">帧序列（按 <c>seq</c> 升序）。</param>
public sealed record AggregateRound(int Round, IReadOnlyList<AggregateFrame> Frames);

/// <summary>
/// 单行落库产物（Stream 域**唯一**一条记录）：身份键 / 位置键 / 聚合载荷。
/// <para>位置键供读侧 overlay 同桶合并；身份键供物化入口按身份模板反解；聚合载荷 = 整个分区的嵌套帧。</para>
/// </summary>
/// <param name="IdentityKey">身份键（单键 = 分区键）。</param>
/// <param name="PositionKey">位置键。</param>
/// <param name="PayloadJson">聚合载荷 JSON（分区 → 轮 → 帧 嵌套）。</param>
public sealed record StreamAggregate(string IdentityKey, string PositionKey, string PayloadJson)
{
    /// <summary>载荷顶层字段面（读投影 overlay 的 <c>Fields</c>；载荷由累积器序列化 ⇒ 必为合法 JSON）。</summary>
    public IReadOnlyDictionary<string, string> Flatten()
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(PayloadJson);
        }
        catch
        {
            return fields;
        }

        if (parsed is not JsonObject obj)
        {
            return fields;
        }

        foreach (var pair in obj)
        {
            if (pair.Value is null)
            {
                continue;
            }

            fields[pair.Key] = pair.Value is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : pair.Value.ToJsonString();
        }

        return fields;
    }
}
