using DataCenter.Abstractions.Cells;
using DataCenter.Protocol.Generation;
using DataCenter.Protocol.Registry;
using DataCenter.Protocol.Roots;

namespace DataCenter.Hosting;

/// <summary>
/// 换代装配上下文（<see cref="DataCenterHostOptions.RootFactory"/> 的输入）—— 宿主注入的新代数装配入口据此装配新根
/// （S4-2：扩展装配产物在新 ALC 内重建；参数化实例化，权威: 设计文档 §4 A5）。
/// <para>
/// <b>ALC 边界（硬约束）</b>：根 / 状态真源 / 投影器 / 单根守卫必须始终由<b>默认 ALC</b> 承载 ——
/// <c>DataCenterRoot</c> 的单根守卫（<c>RootInstanceGuard</c>）与端口类型身份均为程序集级；
/// 若在新 scoped ALC 里装载 <c>DataCenter.Core</c> 副本，守卫各持一份计数 ⇒ DF8 静默失效 + 跨 ALC 类型身份分裂。
/// 故本上下文所称"新代数"只指<b>扩展装配产物</b>（运行时槽内的扩展域对象）在新 ALC 中重建，
/// 根实例本身仍须在默认 ALC 装配。
/// </para>
/// <para>
/// <b>状态连续（Seq）</b>：<see cref="RootCell"/> 与宿主 <c>StateCenter</c> 跨代数复用，
/// 故同一 scope 的值与其版本序列天然连续（"带着运行数据换代"）；新根必须挂同一 <see cref="RootCell"/>
/// （否则校验 fail-fast：换代不得变成"换空真源"）。
/// </para>
/// <para>
/// <b>旧根已释放</b>：单根不变量（进程内根 ≤ 1）要求旧代数根先释放、再装配新代数，
/// 故本上下文只暴露旧代际读值（<see cref="PreviousGeneration"/>），不暴露已释放的旧根实例（无只读价值）。
/// </para>
/// </summary>
/// <param name="RootCell">状态真源（跨代数复用；新根必须挂同一实例）。</param>
/// <param name="Registry">装配期固化的类型/键注册表（与投影器同源）。</param>
/// <param name="Generation">本次换代的目标代际（已推进：generation+1 / fingerprint 已更新）。</param>
/// <param name="PreviousGeneration">上一代代际读值（已释放旧根的代际快照；诊断/迁移判据用）。</param>
/// <param name="Slots">新代数的运行时槽承载（扩展装配产物的重建落点）。</param>
public sealed record RootSwapContext(
    IDataCell RootCell,
    IStateRegistry Registry,
    IStateGeneration Generation,
    IStateGeneration PreviousGeneration,
    IRuntimeSlot Slots);
