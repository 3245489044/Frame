namespace Strategy.Observability.Ports;

/// <summary>
/// 执行后端接缝（CF-4，L3 极小接口）：端口的**执行面**经本接口外置给注入实现
/// （L4 <c>PortCircuitEngine</c> = 电路引擎实现），使 L3 内核**不引用框架**而仍可把执行切到电路。
/// <para>
/// 唯一消费点 = <see cref="PortCircuitBinding"/>；缺省未注入 ⇒ 绑定**回退** <see cref="PortEvaluatorBinding"/>
/// （旧路径），行为零变更（迁移窗口内单点可逆）。
/// </para>
/// <para>判据载体 = 结构事实：<see cref="PortSpec"/>（唯一规格模型）+ 入参 + 注入袋，零领域语义、零框架类型。</para>
/// </summary>
public interface IPortExecutionBackend
{
    /// <summary>
    /// 执行端口（返回产物取值根；可为 null）。
    /// <para>语义契约与 <see cref="IPortBinding.InvokeAsync"/> 同口径：调用方按端口键 + 位置/具名实参调用，
    /// 后端负责产出该端口的交付产物。</para>
    /// </summary>
    /// <param name="spec">唯一规格模型（端口键 + 实现归属 + 形参表 + 指令序列 + 声明面引用）。</param>
    /// <param name="input">调用输入（位置实参 / 具名实参 / 源文本）。</param>
    /// <param name="injection">装配注入袋（工厂表等运行期成员；缺表且声明了 factory 即 fail-fast）。</param>
    /// <param name="ct">取消令牌。</param>
    ValueTask<object?> ExecuteAsync(
        PortSpec spec, IPortInput input, PortInjection injection, CancellationToken ct = default);
}
