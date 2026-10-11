using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 传递载荷（容器 → 成员的下行输入面）—— 容器自身**不持有**该载荷（入站对本容器标
/// <c>FeedTarget.State</c>），仅作为投喂内容下发给目标成员。
/// </summary>
/// <param name="Text">载荷文本（本轮提示词 / 输入）。</param>
/// <param name="Origin">来源标注（结构事实，供渲染分段；缺省空）。</param>
public sealed record TransferPayload(string Text, string Origin);

/// <summary>
/// 单条传递项（上行贡献的**分类载荷**）—— 由 <c>[Flow] TransferContribute</c> 的 <c>merge_fields</c>
/// 步产出；分类/稳定度取值 ∈ <c>Config\nodes\transfer.ushar</c>（词表单源）。
/// </summary>
/// <param name="Owner">成员键（贡献主体）。</param>
/// <param name="Kind">成员种类词（如 task）。</param>
/// <param name="Class">字段分类（window / code / semantic）。</param>
/// <param name="Volatility">稳定度（stable / volatile）。</param>
/// <param name="Key">字段键。</param>
/// <param name="Value">字段值（可空）。</param>
public sealed record TransferItem(string Owner, string Kind, string Class, string Volatility, string Key, string? Value);

/// <summary>
/// 通用传递框架调用面（**唯一入口**；内部驱动声明流 <c>[Flow] TransferContribute</c> / <c>TransferDispatch</c>）。
/// <para>
/// 编排真源 = <c>Config\workflows\transfer-contribute.ushar</c> / <c>transfer-dispatch.ushar</c>；
/// 本接口只是两条流的**调用入口**，不含顺序语义；实现体委托声明流执行（Agent 侧），
/// 使 Frame 侧调用点不反向依赖 Agent 程序集。
/// </para>
/// <para>
/// 通用性：层级由 <paramref name="edgeType"/> 参数化（<c>history_owns_task</c> / <c>cross_db_link</c> /
/// <c>task_owns_task</c>），不由「领域模块」分叉（history↔task 为首例，task↔node 为第二例）。
/// </para>
/// <para>装配可空：未装配 ⇒ 调用方按「可见化 + 显式 ∅」处理，不静默。</para>
/// </summary>
public interface ITransferFlow
{
    /// <summary>
    /// 上行贡献（成员 → 容器）：一次运行完成「枚举成员 → 取窗口 → 取状态 → 合并」。
    /// </summary>
    /// <param name="containerKey">容器键（history 侧 = historyId）。</param>
    /// <param name="edgeType">成员归属边类型（<c>history_owns_task</c> / <c>cross_db_link</c>）。</param>
    /// <param name="windowBudget">窗口预算（最近 N 条；≤0 ⇒ 由实现方取缺省上限，禁无界）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>分类后的传递项（只读派生；无事实 ⇒ 空集，不伪造）。</returns>
    Task<IReadOnlyList<TransferItem>> ContributeAsync(
        string containerKey, string edgeType, int windowBudget = 0, CancellationToken ct = default);

    /// <summary>
    /// 下行分发（容器 → 成员）：一次运行完成「枚举成员 → 确保成员 → 逐个分发载荷」。
    /// </summary>
    /// <param name="containerKey">容器键（history 侧 = historyId）。</param>
    /// <param name="edgeType">成员归属边类型。</param>
    /// <param name="payload">载荷（容器自身不持有；标 <c>FeedTarget.State</c>）。</param>
    /// <param name="workspaceSign">建成员信号（无成员时用于建成员；可空）。</param>
    /// <param name="ct">取消令牌。</param>
    Task DispatchAsync(
        string containerKey, string edgeType, TransferPayload payload,
        string? workspaceSign = null, CancellationToken ct = default);
}
