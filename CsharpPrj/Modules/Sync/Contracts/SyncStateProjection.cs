using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Modules.Sync.Contracts;

/// <summary>
/// 下行唯一载荷：12 字段可见状态投影。
/// <para>
/// <b>不得增删字段。</b>该类型是协议的字节级契约，字段集合、顺序、宽度与编码全部写死在
/// <see cref="SyncStateProjectionCodec"/>；任何一侧多一个字段，双端 <c>StateHash</c> 必然对不上。
/// </para>
/// <para>禁止在投影里加入规则原文、条件词表、对手配置、侦察片段（设计文档 §6.4：只下行可见结果态）。</para>
/// </summary>
public sealed class SyncStateProjection
{
    /// <summary>决策 tick（字段 1）。</summary>
    public long Tick { get; init; }

    /// <summary>当前同步预算（字段 2）。</summary>
    public int Sync { get; init; }

    /// <summary>同步预算上限（字段 3）。</summary>
    public int Smax { get; init; }

    /// <summary>血量，千分制 <c>[0, SnapshotHpScale]</c>（字段 4）。</summary>
    public int Hp { get; init; }

    /// <summary>行动条，千分制 <c>[0, AtbScale]</c>（字段 5）。</summary>
    public int AtB { get; init; }

    /// <summary>技能槽 0 剩余次数（字段 6）。</summary>
    public int Pp0 { get; init; }

    /// <summary>技能槽 1 剩余次数（字段 7）。</summary>
    public int Pp1 { get; init; }

    /// <summary>技能槽 2 剩余次数（字段 8）。</summary>
    public int Pp2 { get; init; }

    /// <summary>技能槽 3 剩余次数（字段 9）。</summary>
    public int Pp3 { get; init; }

    /// <summary>保护窗口结束决策 tick；<c>0</c> 表示无窗口（字段 10）。</summary>
    public long GuardUntilTick { get; init; }

    /// <summary>可见线索字节：<c>0=None / 1=Projectile / 2=Smoke / 3=Melee</c>（字段 11）。</summary>
    public byte Telegraph { get; init; }

    /// <summary>撤离点是否激活（字段 12，编码为 <c>0/1</c> 单字节）。</summary>
    public bool ExtractActive { get; init; }
}

/// <summary>投影的一个字段的编码布局（只读元数据，供对拍与文档生成，不参与运行时逻辑）。</summary>
public readonly record struct ProjectionFieldSpec(string Name, string Type, int Offset, int Width);

/// <summary>
/// 12 字段投影的固定宽度编码布局：小端、无填充、无对齐尾巴，总长 <see cref="EncodedBytes"/>。
/// <para>顺序即字段表顺序；<b>禁止依赖序列化器的默认行为</b>（不用 JSON、不用反射、不用 <c>BinaryFormatter</c>）。</para>
/// </summary>
public static class SyncStateProjectionLayout
{
    /// <summary>字段数（冻结为 12）。</summary>
    public const int FieldCount = 12;

    /// <summary>一次规范化编码的固定字节数（8+4+4+4+4+4×4+8+1+1 = 50）。</summary>
    public const int EncodedBytes = 50;

    /// <summary>字段表：名字、编码类型、字节偏移、字节宽度。</summary>
    public static readonly IReadOnlyList<ProjectionFieldSpec> Fields = new ProjectionFieldSpec[]
    {
        new("Tick", "int64", 0, 8),
        new("Sync", "int32", 8, 4),
        new("Smax", "int32", 12, 4),
        new("Hp", "int32", 16, 4),
        new("AtB", "int32", 20, 4),
        new("Pp0", "int32", 24, 4),
        new("Pp1", "int32", 28, 4),
        new("Pp2", "int32", 32, 4),
        new("Pp3", "int32", 36, 4),
        new("GuardUntilTick", "int64", 40, 8),
        new("Telegraph", "uint8", 48, 1),
        new("ExtractActive", "uint8(bool)", 49, 1),
    };
}

/// <summary>
/// 投影的规范化编解码：固定宽度 + 小端 + 字段表顺序。
/// <para>
/// 违约表现：<c>Decode(Encode(x))</c> 与原结构逐字段不等（H01）；任一侧使用不同端序/填充/顺序时，
/// <see cref="StateHash"/> 在编码层即分歧，双端对账必然不一致。
/// </para>
/// </summary>
public static class SyncStateProjectionCodec
{
    /// <summary>把投影写入 <paramref name="destination"/> 的前 50 字节。</summary>
    public static void Encode(SyncStateProjection projection, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(projection);

        if (destination.Length < SyncStateProjectionLayout.EncodedBytes)
        {
            throw new ArgumentException(
                $"投影编码需要 {SyncStateProjectionLayout.EncodedBytes} 字节，实际 {destination.Length}。",
                nameof(destination));
        }

        BinaryPrimitives.WriteInt64LittleEndian(destination[0..8], projection.Tick);
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..12], projection.Sync);
        BinaryPrimitives.WriteInt32LittleEndian(destination[12..16], projection.Smax);
        BinaryPrimitives.WriteInt32LittleEndian(destination[16..20], projection.Hp);
        BinaryPrimitives.WriteInt32LittleEndian(destination[20..24], projection.AtB);
        BinaryPrimitives.WriteInt32LittleEndian(destination[24..28], projection.Pp0);
        BinaryPrimitives.WriteInt32LittleEndian(destination[28..32], projection.Pp1);
        BinaryPrimitives.WriteInt32LittleEndian(destination[32..36], projection.Pp2);
        BinaryPrimitives.WriteInt32LittleEndian(destination[36..40], projection.Pp3);
        BinaryPrimitives.WriteInt64LittleEndian(destination[40..48], projection.GuardUntilTick);
        destination[48] = projection.Telegraph;
        destination[49] = projection.ExtractActive ? (byte)1 : (byte)0;
    }

    /// <summary>分配并返回一次规范化编码（只在非热路径使用）。</summary>
    public static byte[] EncodeToBytes(SyncStateProjection projection)
    {
        var bytes = new byte[SyncStateProjectionLayout.EncodedBytes];
        Encode(projection, bytes);
        return bytes;
    }

    /// <summary>按字段表顺序还原投影。</summary>
    public static SyncStateProjection Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < SyncStateProjectionLayout.EncodedBytes)
        {
            throw new ArgumentException(
                $"投影编码长度为 {SyncStateProjectionLayout.EncodedBytes}，实际 {source.Length}。",
                nameof(source));
        }

        return new SyncStateProjection
        {
            Tick = BinaryPrimitives.ReadInt64LittleEndian(source[0..8]),
            Sync = BinaryPrimitives.ReadInt32LittleEndian(source[8..12]),
            Smax = BinaryPrimitives.ReadInt32LittleEndian(source[12..16]),
            Hp = BinaryPrimitives.ReadInt32LittleEndian(source[16..20]),
            AtB = BinaryPrimitives.ReadInt32LittleEndian(source[20..24]),
            Pp0 = BinaryPrimitives.ReadInt32LittleEndian(source[24..28]),
            Pp1 = BinaryPrimitives.ReadInt32LittleEndian(source[28..32]),
            Pp2 = BinaryPrimitives.ReadInt32LittleEndian(source[32..36]),
            Pp3 = BinaryPrimitives.ReadInt32LittleEndian(source[36..40]),
            GuardUntilTick = BinaryPrimitives.ReadInt64LittleEndian(source[40..48]),
            Telegraph = source[48],
            ExtractActive = source[49] == 1,
        };
    }
}

/// <summary>
/// FNV-1a 64 位：全协议唯一哈希算法（状态哈希与规则集哈希共用）。
/// <para>初值 <see cref="OffsetBasis"/>、乘数 <see cref="Prime"/> 写死，不得替换为 <c>GetHashCode</c> 或随机种子。</para>
/// </summary>
public static class Fnv1a64
{
    /// <summary>FNV-1a 64 初值 = 14695981039346656037（<c>0xCBF29CE484222325</c>）。</summary>
    public const ulong OffsetBasis = 14695981039346656037UL;

    /// <summary>FNV-1a 64 乘数 = 1099511628211（<c>0x100000001B3</c>）。</summary>
    public const ulong Prime = 1099511628211UL;

    /// <summary>按 FNV-1a 64 逐字节累乘。</summary>
    public static ulong Hash(ReadOnlySpan<byte> data)
    {
        ulong hash = OffsetBasis;
        for (int i = 0; i < data.Length; i++)
        {
            hash ^= data[i];
            hash *= Prime;
        }

        return hash;
    }

    /// <summary>对 UTF-8 文本做同一哈希（规则集哈希的唯一文本入口）。</summary>
    public static ulong HashUtf8(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Hash(Encoding.UTF8.GetBytes(text));
    }
}

/// <summary>
/// 状态哈希：对 12 字段投影的 <b>规范化 50 字节</b>做 FNV-1a 64。
/// <para>
/// <b>输入域</b> = <see cref="SyncStateProjectionCodec"/> 编码结果（字段表顺序、小端、含 <c>Tick</c>），
/// 不混入会话号、消息类型、命令 id、规则集、时间戳。两端只要按同一字段表编码，同一 tick 的哈希逐字节一致。
/// </para>
/// <para>违约表现（R6）：任一字段不同 → 至少一位不同 → 双端对账记 <c>sync.hashMismatch</c>。</para>
/// </summary>
public readonly struct StateHash : IEquatable<StateHash>
{
    /// <summary>零值表示“尚未计算/中止”，不是合法状态哈希。</summary>
    public StateHash(ulong value) => Value = value;

    /// <summary>哈希原始值。</summary>
    public ulong Value { get; }

    /// <summary>对投影做规范化编码后哈希。</summary>
    public static StateHash Compute(SyncStateProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new StateHash(Fnv1a64.Hash(SyncStateProjectionCodec.EncodeToBytes(projection)));
    }

    /// <summary>对已编码的规范化字节做哈希（用于逐字节相等断言）。</summary>
    public static StateHash ComputeCanonical(ReadOnlySpan<byte> canonicalProjection)
        => new StateHash(Fnv1a64.Hash(canonicalProjection));

    /// <inheritdoc />
    public bool Equals(StateHash other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is StateHash other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (int)(Value ^ (Value >> 32));

    /// <inheritdoc />
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "0x{0:X16}", Value);
}
