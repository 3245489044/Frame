namespace BasicMethod.Declarative.Ledger;

/// <summary>成员对象定位结果（失败原因**显式化**；文案留消费方，机制层不内联文案）。</summary>
public enum LedgerLocateStatus
{
    /// <summary>定位成功（<c>Start</c> / <c>End</c> 有效，闭区间含花括号）。</summary>
    Found = 0,

    /// <summary>键未登记（<c>"key":</c> 不存在）。</summary>
    KeyMissing = 1,

    /// <summary>该键下未找到成员名（形态漂移）。</summary>
    MemberMissing = 2,

    /// <summary>成员值非对象（缺对象体）。</summary>
    BodyMissing = 3,

    /// <summary>对象体花括号不配平（文本已损坏）。</summary>
    Unbalanced = 4,
}

/// <summary>
/// 台账**文本区间改写**（原地改写口径）：按「键 → 成员名 → 对象体」锚点定位，再以**花括号配平**取出
/// 成员对象的原文区间；替换时**其余字节逐字保留**（不重排 / 不重序列化整份文本 ⇒ diff 只落在被改区间）。
/// <para>
/// 定位失败一律以 <see cref="LedgerLocateStatus"/> 回报（不抛文案），由消费方映射为域文案（文案单源）；
/// <see cref="ReplaceMemberObject"/> 遇非 <see cref="LedgerLocateStatus.Found"/> 即抛
/// <see cref="DeclarationViolationException"/>（阻断，防静默漏改）。
/// </para>
/// </summary>
public static class LedgerTextEditor
{
    /// <summary>定位「<c>"key"</c> → <c>"member"</c> → 首个 <c>{</c> → 配平 <c>}</c>」的对象体区间（闭区间）。</summary>
    public static (LedgerLocateStatus Status, int Start, int End) LocateMemberObject(
        string text, string key, string member)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(member);

        var keyIndex = text.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
        if (keyIndex < 0) return (LedgerLocateStatus.KeyMissing, -1, -1);

        var memberIndex = text.IndexOf("\"" + member + "\"", keyIndex, StringComparison.Ordinal);
        if (memberIndex < 0) return (LedgerLocateStatus.MemberMissing, -1, -1);

        var openIndex = text.IndexOf('{', memberIndex);
        if (openIndex < 0) return (LedgerLocateStatus.BodyMissing, -1, -1);

        var depth = 0;
        for (var index = openIndex; index < text.Length; index++)
            switch (text[index])
            {
                case '{':
                    depth++;
                    break;

                case '}':
                    depth--;
                    if (depth == 0) return (LedgerLocateStatus.Found, openIndex, index);
                    break;
            }

        return (LedgerLocateStatus.Unbalanced, -1, -1);
    }

    /// <summary>
    /// 整块替换成员对象体（其余字节逐字保留）。<paramref name="changed"/> = false 表示原本即同块（**幂等**）。
    /// </summary>
    public static string ReplaceMemberObject(
        string text, string key, string member, string block, out bool changed)
    {
        var (status, start, end) = LocateMemberObject(text, key, member);
        if (status != LedgerLocateStatus.Found)
            throw new DeclarationViolationException(
                $"成员对象定位失败（{status}）：{key}.{member}", [status.ToString()]);

        if (text.AsSpan(start, end - start + 1).SequenceEqual(block))
        {
            changed = false;
            return text;
        }

        changed = true;
        return string.Concat(text.AsSpan(0, start), block, text.AsSpan(end + 1));
    }
}
