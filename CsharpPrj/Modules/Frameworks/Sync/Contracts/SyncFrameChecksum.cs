using System.Buffers.Binary;

namespace Modules.Sync.Contracts;

/// <summary>
/// 逐帧校验（H14 契约缺口）：FNV-a64（<see cref="Fnv1a64"/>，初值 <see cref="Fnv1a64.OffsetBasis"/>、
/// 乘数 <see cref="Fnv1a64.Prime"/>，与 <see cref="StateHash"/> 同算法）两层检查。
/// <para>
/// 第 1 层：校验和覆盖 <c>58 字节 = Tick(8) + 投影(<see cref="SyncStateProjectionLayout.EncodedBytes"/>=50)</c>，
/// 明确排除信封末尾的 <c>StateHash(8)</c> 字节，用于发现对 Tick / 投影的篡改。
/// </para>
/// <para>
/// 为什么不覆盖 StateHash：若覆盖 StateHash，篡改 Tick 或投影时校验和与 StateHash 会同时变化，
/// 无法区分「整帧校验失败」与「状态哈希不匹配」。
/// </para>
/// <para>
/// 第 2 层：校验和排除 StateHash 会留下一个洞——单独篡改帧内 <c>StateHash</c> 字段时第 1 层不变。
/// 因此必须用投影重算 StateHash，再与帧内 StateHash 逐字节比对，用于发现对 StateHash 字段本身的篡改。
/// 两层任一不符都以 <see cref="RejectReason.ChecksumMismatch"/> 拒绝该帧、且不得提交（合并为一个错误码）。
/// </para>
/// <para>
/// 58 字节覆盖范围（= 66 - 8 = <see cref="SyncProtocol.MaxFrameBytes"/> - 8）当前为契约缺口的临时取值，
/// 待契约确认后调整；<see cref="StateHash"/> 的既有语义不变（仍只覆盖 50 字节投影、含 <c>Tick</c>）。
/// </para>
/// </summary>
public static class SyncFrameChecksum
{
    /// <summary>帧首 Tick 的字节数；投影从该偏移开始。</summary>
    public const int ProjectionOffset = 8;

    /// <summary>第 1 层校验和覆盖的字节数：<c>Tick(8) + 投影(50) = 58</c>（不含 StateHash）。</summary>
    public const int CoveredBytes = ProjectionOffset + SyncStateProjectionLayout.EncodedBytes;

    /// <summary>信封内 StateHash 字段的起始偏移（= <see cref="CoveredBytes"/>）。</summary>
    public const int StateHashOffset = CoveredBytes;

    /// <summary>信封内 StateHash 字段的字节数（小端存放）。</summary>
    public const int StateHashBytes = 8;

    /// <summary>两层检查所需的完整帧长度：<c>8 + 50 + 8 = 66</c>。</summary>
    public const int VerifiedFrameBytes = StateHashOffset + StateHashBytes;

    /// <summary>校验失配的稳定错误名（对应 <see cref="RejectReason.ChecksumMismatch"/>，两层检查的合并结果）。</summary>
    public const string StableErrorCode = "checksum-mismatch";

    /// <summary>第 1 层：对帧的前 <see cref="CoveredBytes"/> 字节做 FNV-1a64。</summary>
    public static ulong Compute(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < CoveredBytes)
        {
            throw new ArgumentException(
                $"帧长度至少 {CoveredBytes} 字节（Tick 8 + 投影 {SyncStateProjectionLayout.EncodedBytes}），实际 {frame.Length}。",
                nameof(frame));
        }

        return Fnv1a64.Hash(frame[..CoveredBytes]);
    }

    /// <summary>
    /// 两层校验：第 1 层校验和 + 第 2 层 StateHash 重算。全部通过返回 <c>null</c>，
    /// 任一层不符返回 <see cref="RejectReason.ChecksumMismatch"/>。
    /// </summary>
    public static RejectReason? Verify(ReadOnlySpan<byte> frame, ulong expectedChecksum)
        => Check(frame, expectedChecksum).Rejection;

    /// <summary>
    /// 两层校验门：任一层不符返回 <see cref="RejectReason.ChecksumMismatch"/> 且 <c>CommitAllowed=false</c>，
    /// 调用方<b>不得提交该帧</b>；只有 <c>CommitAllowed=true</c> 才允许进入提交。
    /// </summary>
    public static SyncFrameChecksumGate Check(ReadOnlySpan<byte> frame, ulong expectedChecksum)
    {
        if (frame.Length < VerifiedFrameBytes)
            return new SyncFrameChecksumGate(RejectReason.ChecksumMismatch, 0UL, false);

        ulong actual = Compute(frame);
        bool checksumOk = actual == expectedChecksum;
        bool stateHashOk = StateHashMatches(frame);
        bool accepted = checksumOk && stateHashOk;
        return new SyncFrameChecksumGate(
            accepted ? null : RejectReason.ChecksumMismatch,
            actual,
            accepted);
    }

    /// <summary>
    /// 第 2 层：用帧内投影（<c>[8..58)</c>）重算 StateHash，与帧内 StateHash（<c>[58..66)</c>，小端）
    /// 逐字节比对；帧长不足 66 或任一不符返回 <c>false</c>。
    /// </summary>
    public static bool StateHashMatches(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < VerifiedFrameBytes)
            return false;

        ReadOnlySpan<byte> projection = frame.Slice(ProjectionOffset, SyncStateProjectionLayout.EncodedBytes);
        ulong recomputed = Fnv1a64.Hash(projection);
        ulong embedded = BinaryPrimitives.ReadUInt64LittleEndian(
            frame.Slice(StateHashOffset, StateHashBytes));
        return recomputed == embedded;
    }
}

/// <summary>校验结果：<paramref name="CommitAllowed"/>=<c>false</c> 时禁止提交该帧。</summary>
public readonly record struct SyncFrameChecksumGate(
    RejectReason? Rejection,
    ulong Checksum,
    bool CommitAllowed);
