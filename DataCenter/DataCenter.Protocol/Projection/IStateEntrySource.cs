using DataCenter.Abstractions.Cells;

namespace DataCenter.Protocol.Projection;

/// <summary>
/// 状态条目（投影输入的最小单位）—— 已从分层全键（<c>{namespace}:{scope}:{key}</c>）解析出的四元组。
/// 由 <see cref="IStateEntrySource"/> 产出；投影器不直接依赖状态中心实现（DF2：一切经端口）。
/// </summary>
/// <param name="Namespace">分层命名空间（形态 <c>{domain}.{tier}</c>，如 <c>{domain}.Session</c>）。</param>
/// <param name="Scope">分区标识（会话/任务/世代等）。</param>
/// <param name="Key">裸键（不含命名空间与 scope 前缀）。</param>
/// <param name="Tier">所属分层标识。</param>
/// <param name="Value">状态值（原样对象；载荷化由 <see cref="IStateValueCodec"/> 负责）。</param>
public readonly record struct StateEntry(
    string Namespace,
    string Scope,
    string Key,
    DataTier Tier,
    object? Value);

/// <summary>
/// 状态枚举结果 —— 分层条目 + 缺口计数（不可解析键数）。
/// 缺口必须<b>显式报告</b>而非静默吞掉（权威: 设计文档 §5.6"静默降级"检测点）。
/// </summary>
/// <param name="Entries">已解析的分层状态条目。</param>
/// <param name="UnparsedKeys">不符合分层全键形态而被跳过的键数（如非分层的对象 KV / 原始键）。</param>
public readonly record struct StateEntryScan(IReadOnlyList<StateEntry> Entries, int UnparsedKeys);

/// <summary>
/// 状态枚举接缝（端口）—— 使投影器只依赖"可枚举的状态"抽象，而不依赖 <c>StateCenter</c> 具体类型。
/// 实现方：Core 的 <c>StateCenterEntrySource</c>。
/// </summary>
public interface IStateEntrySource
{
    /// <summary>枚举状态条目；<paramref name="scope"/> 为 null = 全部分区（根分区 + 全部 scope 分区）。</summary>
    StateEntryScan Enumerate(string? scope);
}

/// <summary>
/// 值↔载荷编解码端口 —— 把运行时值映射为语言无关载荷（跨语言 Adapter 只需理解载荷）。
/// 实现方：Core 的 <c>JsonValueCodec</c>；域可按需定制（如结构化对象 → 独立节点）。
/// </summary>
public interface IStateValueCodec
{
    /// <summary>值 → 载荷（跨语言稳定形态：标量直通，其余经确定性序列化）。</summary>
    object? Encode(object? value);
}
