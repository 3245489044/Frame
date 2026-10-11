using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Operation.HierarchyMethod.History.Conversation;

/// <summary>
/// 单个切分段（切分器产物；载荷恒为 JSON 文档）。
/// <para>
/// <c>seq</c> 取**源索引派生**（段内首条消息下标 + 1）：同输入重跑 ⇒ 同 <c>seq</c>/<c>Digest</c>（幂等）。
/// </para>
/// </summary>
/// <param name="Seq">段序（源索引派生）。</param>
/// <param name="Kind">段类别（模式语义名 / 原类型 / 角色）。</param>
/// <param name="Role">段角色（段内首条）。</param>
/// <param name="Text">段文本（组内拼接）。</param>
/// <param name="Ts">段时刻（段内首条）。</param>
/// <param name="Iteration">所属 LLM 回合序。</param>
/// <param name="ToolCallId">工具调用标识（可空）。</param>
/// <param name="SourceIndex">源下标。</param>
/// <param name="Parent">父（轮）键引用（键引用而非对象引用）。</param>
public sealed record ConversationSegment(
    int Seq,
    string Kind,
    string Role,
    string Text,
    long? Ts,
    int Iteration,
    string? ToolCallId,
    int SourceIndex,
    string? Parent)
{
    /// <summary>段载荷 JSON（字段名固定：<c>kind</c>/<c>role</c>/<c>text</c>/…，供读侧 <c>selection</c> 断言）。</summary>
    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["seq"] = Seq,
        ["kind"] = Kind,
        ["role"] = Role,
        ["text"] = Text,
        ["ts"] = Ts,
        ["iteration"] = Iteration,
        ["toolCallId"] = ToolCallId,
        ["sourceIndex"] = SourceIndex,
        ["parent"] = Parent,
    }, HistoryPayloadJson.Options);

    /// <summary>内容摘要（稳定哈希；同输入 ⇒ 同摘要，供幂等对账）。</summary>
    public string Digest() => HistorySegmenter.ComputeDigest(ToJson());
}
