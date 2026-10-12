namespace Usharp.Semantics;

/// <summary>
/// 声明库中的一条 USharp 声明文档：<b>逻辑名</b>（诊断归因键，如 <c>workflows/task-provision.ushar</c> 或
/// <c>workflows/task-provision</c>）+ <b>文本</b> + 可选 lib 归属（null = <see cref="Syntax.UsharpParser.DefaultLib"/>）。
/// <para>
/// 逻辑名而非物理路径：声明可来自磁盘（<see cref="FileDeclarationStore"/>）或数据库
/// （DB 实现经本端口注入），装载面不感知来源（对齐「USharp 编排入库」——盘/库是两种实现，不是两条链路）。
/// </para>
/// </summary>
/// <param name="LogicalName">逻辑名（去 `<c>.ushar</c>` 与否均可，由实现约定；装载/去重键）。</param>
/// <param name="Text">声明文本（原样，不预解析）。</param>
/// <param name="Lib">库归属（跨 lib 的 <c>[CR]</c> 解析口径，见 <see cref="UsharpSourceLoader"/>）。</param>
/// <param name="SourcePath">诊断归因路径（可选）：文件库给真实路径，库实现可给逻辑名或 null。</param>
public sealed record DeclarationEntry(string LogicalName, string Text, string? Lib = null, string? SourcePath = null);

/// <summary>
/// 声明库端口：USharp 声明文档（逻辑名 → 文本）的**来源抽象**（单一实现面）。
/// <para>
/// 两种实现并列：<see cref="FileDeclarationStore"/>（现磁盘语义，迁移期回退面）与数据库实现
/// （由上层适配层实现本端口，声明入库后为唯一真源）。运行期装载一律经本端口 ——
/// 禁在装载面直接读盘（对齐「声明即数据 / 单一声明源」，盘仅作导入/回退）。
/// </para>
/// <para>
/// 本端口**保持零外部依赖**（纯字符串 + BCL Task）：不感知数据库/UR/节点框架类型，
/// 数据库实现落在上层适配层（耦合方向 = 适配层 → 本端口）。
/// </para>
/// </summary>
public interface IDeclarationStore
{
    /// <summary>列出全部声明文档（声明序由实现约定；装载面按逻辑名排序以便复现）。</summary>
    Task<IReadOnlyList<DeclarationEntry>> ListAsync(CancellationToken ct = default);

    /// <summary>取一条声明文档；不存在返回 <c>null</c>（不抛 —— 「未声明」是合法状态，由调用方降级口径处理）。</summary>
    Task<DeclarationEntry?> GetAsync(string logicalName, CancellationToken ct = default);

    /// <summary>该逻辑名是否已存在。</summary>
    Task<bool> ExistsAsync(string logicalName, CancellationToken ct = default);

    /// <summary>写入/覆盖一条声明文档（导入或运行期生成用；写入语义由实现约定）。</summary>
    Task PutAsync(DeclarationEntry entry, CancellationToken ct = default);
}
