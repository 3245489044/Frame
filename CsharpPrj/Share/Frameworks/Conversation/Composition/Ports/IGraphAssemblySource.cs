using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Share.Composition.Ports;

/// <summary>
/// 图组装端口 —— 确定性图组装（任务 + 关联节点 + 引用边 + 入队）的抽象。
/// 由<b>消费侧适配器</b>实现并包装 Frame 的确定性写图服务（内核不反向引用 Frame）。
/// <para>
/// 与文本 / 模板组装的差异：本端口产出 <see cref="CompositionResult{T}"/>（产物 + 部件集 + <b>带诊断快照</b>），
/// 保留幂等 / 部分成功 / 诊断收集语义，<b>不套文本槽拼接模型</b>。
/// </para>
/// <para>
/// 请求类型 <typeparamref name="TRequest"/> 由内核保持不透明（各消费域的图请求字段差异大，
/// 强行收敛为通用部件会丢失领域字段），内核只规定<b>产物形态</b>与<b>诊断契约</b>。
/// </para>
/// </summary>
/// <typeparam name="TRequest">领域图组装请求（如 <c>CreateTaskGraphRequest</c>）。</typeparam>
public interface IGraphAssemblySource<in TRequest>
{
    /// <summary>
    /// 执行一次确定性图组装。语义约束（实现方必须保持）：
    /// 幂等（已存在跳过）、部分成功不回滚（失败逐条记诊断）、未装配端口记诊断而非抛错。
    /// </summary>
    /// <param name="request">领域图组装请求。</param>
    /// <param name="ctx">装配上下文（任务键 / 作用域键）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<CompositionResult<GraphAssemblyOutcome>> AssembleAsync(
        TRequest request, IAssemblyContext ctx, CancellationToken ct = default);
}
