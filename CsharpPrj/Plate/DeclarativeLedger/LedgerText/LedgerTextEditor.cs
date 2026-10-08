using System.Text;
using System.Text.Json;

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

    /// <summary>
    /// 整块替换 JSON Pointer 所指**值区间**（其余字节逐字保留）。定位失败即抛
    /// <see cref="DeclarationViolationException"/>（阻断，防静默漏改）；<paramref name="changed"/> = false
    /// 表示原本即同值（**幂等**）。
    /// </summary>
    public static string ReplaceValueSpan(string text, string pointer, string replacement, out bool changed)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        if (!TryLocateValueSpan(text, pointer, out var start, out var length))
            throw new DeclarationViolationException($"值区间定位失败：{pointer}", [pointer]);

        if (text.AsSpan(start, length).SequenceEqual(replacement))
        {
            changed = false;
            return text;
        }

        changed = true;
        return string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(start + length));
    }

    /// <summary>
    /// 定位 JSON Pointer 所指**值的原始文本区间**（<paramref name="length"/> = 字符长度；失败 → false）。
    /// <para>
    /// 指针 = <c>/</c> 分隔键路径（如 <c>$defs/expr/properties</c>；前置 <c>/</c> 可省）；扫描按 **token 词法**
    /// 进行——**字符串字面量内的伪键名不误命中**（替代裸 <c>IndexOf("\"key\":")</c> 的假命中面）；字节偏移经
    /// UTF-8 换算为**字符**下标（非 ASCII 前置文本亦正确）。供「值区间原地改写」消费（其余字节逐字保留）。
    /// </para>
    /// </summary>
    public static bool TryLocateValueSpan(string text, string pointer, out int start, out int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(pointer);

        start = 0;
        length = 0;

        var segments = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return false;

        var containers = new Stack<string>();
        var pending = string.Empty;

        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes);
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    containers.Push(pending);      // 该容器的键名（根容器为 ""）
                    pending = string.Empty;
                    break;

                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    if (containers.Count > 0) containers.Pop();
                    break;

                case JsonTokenType.PropertyName:
                    pending = reader.GetString() ?? string.Empty;
                    if (!IsTargetPath(containers, pending, segments) || !reader.Read()) break;

                    var byteStart = (int)reader.TokenStartIndex;
                    if (reader.TokenType is not (JsonTokenType.StartArray or JsonTokenType.StartObject))
                    {
                        SetByteRange(bytes, byteStart, (int)reader.ValueSpan.Length, out start, out length);
                        return true;
                    }

                    // 容器值：按嵌套计数找到配对的收尾 token，范围 = [起始, 收尾末字符)
                    var nest = 0;
                    while (reader.Read())
                    {
                        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
                        {
                            nest++;
                        }
                        else if (reader.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject)
                        {
                            if (nest == 0)
                            {
                                SetByteRange(bytes, byteStart, (int)reader.BytesConsumed - byteStart, out start, out length);
                                return true;
                            }

                            nest--;
                        }
                    }

                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// UTF-8 字节范围 → **字符**范围（非 ASCII 前置文本下两者不等，须显式换算）。
    /// 供「按 <see cref="System.Text.Json.Utf8JsonReader"/> 获取字节偏移，再切 <see cref="string"/> 文本」的消费共用。
    /// </summary>
    public static void Utf8ByteRangeToCharRange(byte[] bytes, int byteStart, int byteLength, out int start, out int length)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        start = Encoding.UTF8.GetCharCount(bytes, 0, byteStart);
        length = Encoding.UTF8.GetCharCount(bytes, 0, byteStart + byteLength) - start;
    }

    /// <summary>本类内部调用位（保持短名）。</summary>
    private static void SetByteRange(byte[] bytes, int byteStart, int byteLength, out int start, out int length)
        => Utf8ByteRangeToCharRange(bytes, byteStart, byteLength, out start, out length);

    /// <summary>当前容器路径（去根哨兵，自内向外）+ 待定键 == 目标键路径？（不依赖读者深度口径）</summary>
    private static bool IsTargetPath(Stack<string> containers, string name, string[] segments)
    {
        if (containers.Count - 1 != segments.Length - 1) return false;
        if (!string.Equals(name, segments[^1], StringComparison.Ordinal)) return false;

        var index = segments.Length - 2;
        foreach (var container in containers)
        {
            if (index < 0) break;
            if (!string.Equals(container, segments[index], StringComparison.Ordinal)) return false;
            index--;
        }

        return index == -1;
    }
}
