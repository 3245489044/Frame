namespace Modules.Sync.Contracts;

/// <summary>
/// 帧分片/重组的最小 API（H10 契约缺口）。
/// <para>
/// 单帧上限 <see cref="SyncProtocol.MaxFrameBytes"/> = 66 与单片上限 <see cref="SyncProtocol.MaxFragmentBytes"/> = 50
/// 当前为契约缺口的临时取值，待契约确认后调整；66 与 <see cref="SyncMessageLayout"/> 的 Snapshot 宽度
/// <c>8 + 50 + 8</c> 一致，50 与投影编码宽度 <see cref="SyncStateProjectionLayout.EncodedBytes"/> 一致。
/// </para>
/// <para>
/// 失败一律返回显式失败结果（<c>Succeeded=false</c> + 稳定错误码），<b>绝不返回部分重组内容</b>，
/// 缺片（片数不足或出现空片）返回 <see cref="MissingFragmentCode"/>。
/// </para>
/// </summary>
public static class SyncFrameFragmentation
{
    /// <summary>缺片：重组时片数不足或出现空片（fail-closed，不返回部分内容）。</summary>
    public const string MissingFragmentCode = "missing-fragment";

    /// <summary>整帧超过 <see cref="SyncProtocol.MaxFrameBytes"/>。</summary>
    public const string FrameTooLargeCode = "frame-too-large";

    /// <summary>单片上限非法（≤0 或大于整帧上限）。</summary>
    public const string InvalidFragmentSizeCode = "invalid-fragment-size";

    /// <summary>空帧无法分片。</summary>
    public const string EmptyFrameCode = "empty-frame";

    /// <summary>
    /// 按 <paramref name="maxFragmentBytes"/> 把一帧切成<b>按序</b>片段。
    /// 66 字节帧在默认 50 字节上限下切成 <c>50 + 16</c> 两片。
    /// </summary>
    public static SyncFragmentSplitResult Split(
        ReadOnlySpan<byte> frame,
        int maxFragmentBytes = SyncProtocol.MaxFragmentBytes)
    {
        if (frame.Length == 0)
            return SyncFragmentSplitResult.Failed(EmptyFrameCode);

        if (frame.Length > SyncProtocol.MaxFrameBytes)
            return SyncFragmentSplitResult.Failed(FrameTooLargeCode);

        if (maxFragmentBytes <= 0 || maxFragmentBytes > SyncProtocol.MaxFrameBytes)
            return SyncFragmentSplitResult.Failed(InvalidFragmentSizeCode);

        int count = (frame.Length + maxFragmentBytes - 1) / maxFragmentBytes;
        var fragments = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            int offset = i * maxFragmentBytes;
            int length = Math.Min(maxFragmentBytes, frame.Length - offset);
            fragments[i] = frame.Slice(offset, length).ToArray();
        }

        return SyncFragmentSplitResult.Success(fragments);
    }

    /// <summary>
    /// 按序重组；片数必须精确等于 <paramref name="expectedFragmentCount"/>，否则按缺片显式失败。
    /// </summary>
    public static SyncFragmentJoinResult Reassemble(
        IReadOnlyList<byte[]>? fragments,
        int expectedFragmentCount)
    {
        if (fragments is null || expectedFragmentCount <= 0 || fragments.Count != expectedFragmentCount)
            return SyncFragmentJoinResult.Failed(MissingFragmentCode);

        int total = 0;
        for (int i = 0; i < fragments.Count; i++)
        {
            byte[]? fragment = fragments[i];
            if (fragment is null || fragment.Length == 0)
                return SyncFragmentJoinResult.Failed(MissingFragmentCode);

            total += fragment.Length;
        }

        if (total > SyncProtocol.MaxFrameBytes)
            return SyncFragmentJoinResult.Failed(FrameTooLargeCode);

        var frame = new byte[total];
        int offset = 0;
        for (int i = 0; i < fragments.Count; i++)
        {
            byte[] fragment = fragments[i];
            fragment.CopyTo(frame, offset);
            offset += fragment.Length;
        }

        return SyncFragmentJoinResult.Success(frame);
    }
}

/// <summary>分片结果：成功携带按序片段，失败携带稳定错误码且不携带片段。</summary>
public readonly record struct SyncFragmentSplitResult(
    bool Succeeded,
    string? Error,
    IReadOnlyList<byte[]>? Fragments)
{
    /// <summary>成功：<paramref name="fragments"/> 按原帧顺序排列。</summary>
    public static SyncFragmentSplitResult Success(IReadOnlyList<byte[]> fragments)
        => new(true, null, fragments);

    /// <summary>失败：只带错误码，不返回部分内容。</summary>
    public static SyncFragmentSplitResult Failed(string error)
        => new(false, error, null);
}

/// <summary>重组结果：成功携带逐字节还原的整帧，失败携带稳定错误码且不携带部分帧。</summary>
public readonly record struct SyncFragmentJoinResult(
    bool Succeeded,
    string? Error,
    byte[]? Frame)
{
    /// <summary>成功：<paramref name="frame"/> 必须与原帧逐字节相等。</summary>
    public static SyncFragmentJoinResult Success(byte[] frame)
        => new(true, null, frame);

    /// <summary>失败：只带错误码，不返回部分重组内容。</summary>
    public static SyncFragmentJoinResult Failed(string error)
        => new(false, error, null);
}
