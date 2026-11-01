using System;
using System.Collections.Generic;

namespace Share.Conversation.View;

/// <summary>锚点策略（声明受控词 <c>always / if-answer / never</c> 的强类型投影）。</summary>
public enum AnchorPolicy
{
    /// <summary>永不作气泡锚点。</summary>
    Never = 0,

    /// <summary>仅当该帧是终答时作锚点。</summary>
    IfAnswer = 1,

    /// <summary>恒作锚点。</summary>
    Always = 2,
}

/// <summary>
/// 帧语义行（<see cref="FrameTable"/> 主键 = <see cref="Kind"/>）。
/// <para>列来源 = <c>frame-kinds.json</c> + <c>presentation.json</c> + <c>node-gather.ushar</c> 的 <c>shaping presentation=</c>；编译期合并，运行期只读。</para>
/// </summary>
/// <param name="Kind">帧类别（<c>RecognizedKinds</c> 受控词）。</param>
/// <param name="Class">语义类（统计/折叠用）。</param>
/// <param name="Presentation">唯一呈现身份（→ <c>presentation.json.classes</c> 的键）。</param>
/// <param name="Anchor">锚点策略（装配器读，禁再由启发式推测）。</param>
/// <param name="Archive">是否入史。</param>
/// <param name="Views">可出现于哪些视图。</param>
/// <param name="Fold">默认折叠归属（<c>data-*</c> 输出口径）。</param>
/// <param name="Ordinal">排序口径。</param>
/// <param name="Subject">预留轴（不进身份键）。</param>
public sealed record FrameRow(
    string Kind,
    string Class,
    string Presentation,
    AnchorPolicy Anchor,
    bool Archive,
    IReadOnlyList<string> Views,
    string Fold,
    string Ordinal,
    string Subject);

/// <summary>帧语义表（编译产物，不可变；运行期不再读 JSON）。</summary>
/// <param name="Rows">kind → 行。</param>
/// <param name="Fingerprint">内容指纹（SHA256；非 mtime）。</param>
public sealed record FrameTable(IReadOnlyDictionary<string, FrameRow> Rows, string Fingerprint)
{
    /// <summary>空表（未编译；调用方须显式可见化，不得据此判定「无帧」）。</summary>
    public static readonly FrameTable Empty =
        new(new Dictionary<string, FrameRow>(StringComparer.Ordinal), string.Empty);

    /// <summary>按 kind 取行（未声明 ⇒ false，不伪造默认行）。</summary>
    /// <param name="kind">帧类别。</param>
    /// <param name="row">行（未命中为 null）。</param>
    public bool TryGet(string? kind, out FrameRow? row)
    {
        row = null;
        return !string.IsNullOrWhiteSpace(kind) && Rows.TryGetValue(kind!, out row);
    }

    /// <summary>按 kind 取行；未声明 ⇒ 抛（装配期显式失败）。</summary>
    /// <param name="kind">帧类别。</param>
    public FrameRow Require(string kind)
        => TryGet(kind, out var row)
            ? row!
            : throw new InvalidOperationException($"帧语义未声明：{kind}（FrameTable）");
}
