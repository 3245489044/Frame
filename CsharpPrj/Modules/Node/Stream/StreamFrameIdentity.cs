using System;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Modules.Node.Stream;

/// <summary>
/// 帧级身份（通用；域无关）：所属轮 <see cref="Round"/> + run 级到达序 <see cref="Seq"/> + 承载它的 run 身份
/// <see cref="RunId"/>（旧载荷缺失 ⇒ <c>null</c>，不伪造）。
/// <para>
/// 存在理由（SingleImplementation）：同一身份事实原先有三个独立推导——合并去重键取整帧 JSON 文本、
/// 显示键按字符串前缀拼接、绑定锚另走一套合成——任一处改口径即静默漂移。本类型是**唯一身份载体**：
/// 写侧合并（<see cref="StreamPayloadMerge"/>）、读侧显示键（<see cref="IFrameKeyRenderer"/>）、
/// 绑定锚（调用方）全部从它派生。
/// </para>
/// </summary>
/// <param name="RunId">run 身份（载荷根 <c>runId</c>；旧载荷缺失为 null）。</param>
/// <param name="Round">所属轮次（载荷 <c>rounds[].round</c>）。</param>
/// <param name="Seq">run 级到达序（载荷 <c>seq</c>；跨轮不重置）。</param>
public readonly record struct StreamFrameIdentity(string? RunId, int Round, long Seq)
{
    /// <summary>规范化身份键（合并去重的唯一键形；缺 run 段用 <c>-</c> 占位，不与真实值混淆）。</summary>
    public string Key => (RunId ?? "-") + "|" + Round.ToString(CultureInfo.InvariantCulture)
        + "|" + Seq.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// 帧身份的**唯一解析入口**（帧对象 + 所属轮 + 载荷根 run 身份 → <see cref="StreamFrameIdentity"/>）：
/// 合并/读面/绑定共用一份口径；<c>seq</c> 缺失或非正值 ⇒ 不可识别（调用方按文本键回落并可见化，禁静默并帧）。
/// </summary>
public static class StreamFrameIdentityReader
{
    /// <summary>解析帧身份；<c>false</c> ⇒ 帧不可识别（无到达序）。</summary>
    /// <param name="frame">帧 JSON 对象。</param>
    /// <param name="round">所属轮次（外层轮分组）。</param>
    /// <param name="runId">载荷根 run 身份（可空）。</param>
    /// <param name="schema">载荷字段面差异参数。</param>
    /// <param name="identity">解析产物。</param>
    public static bool TryRead(
        JsonObject frame, int round, string? runId, StreamPayloadSchema schema, out StreamFrameIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(schema);

        identity = default;
        if (!TryReadSeq(frame, schema, out var seq))
        {
            return false;
        }

        identity = new StreamFrameIdentity(runId, round, seq);
        return true;
    }

    /// <summary>解析规范化身份键；<c>null</c> ⇒ 帧不可识别（调用方回落文本键）。</summary>
    /// <param name="frame">帧 JSON 对象。</param>
    /// <param name="round">所属轮次。</param>
    /// <param name="runId">载荷根 run 身份（可空）。</param>
    /// <param name="schema">载荷字段面差异参数。</param>
    public static string? KeyOf(JsonObject frame, int round, string? runId, StreamPayloadSchema schema)
        => TryRead(frame, round, runId, schema, out var identity) ? identity.Key : null;

    private static bool TryReadSeq(JsonObject frame, StreamPayloadSchema schema, out long seq)
    {
        seq = 0;
        if (frame[schema.SeqField] is not JsonValue value)
        {
            return false;
        }

        if (value.TryGetValue<long>(out var number))
        {
            seq = number;
        }
        else if (value.TryGetValue<string>(out var text)
                 && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            seq = parsed;
        }

        return seq > 0;
    }
}
