namespace Modules.Node.Materialization.WritePlan;

/// <summary>
/// 介质写端口（唯一写权按 <c>(Medium, Collection)</c> 分派）：介质实现留宿主，
/// 框架只持有计划模型 + 编排 + 该端口。
/// <para>
/// 纪律：同一 <c>(Medium, Collection)</c> 只允许一个处理器（装配期 fail-fast）；
/// 补偿语义由实现声明（append-only 介质须显式报告不可撤销）。
/// </para>
/// </summary>
public interface IWriteMediumHandler
{
    /// <summary>介质名（宿主词表）。</summary>
    string Medium { get; }

    /// <summary>该处理器拥有的集合集（每集合恰一个写端口）。</summary>
    IReadOnlyCollection<string> Collections { get; }

    /// <summary>应用单步。</summary>
    Task ApplyAsync(NodeWriteWork work, CancellationToken cancellationToken = default);

    /// <summary>补偿单步（失败时按已应用步骤逆序调用）。</summary>
    Task CompensateAsync(NodeWriteWork work, CancellationToken cancellationToken = default);
}
