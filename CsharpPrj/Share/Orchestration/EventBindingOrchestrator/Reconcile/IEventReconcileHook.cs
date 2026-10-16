#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TConfig;

namespace Share.EventBindingOrchestrator.Reconcile;

/// <summary>
/// 事件对账钩子（依赖倒置抽象）：框架侧只暴露「注册契约 + 包裹一次函数调用」两点，
/// 具体对账实现（信号账 / 四判 / 裁决）落在 Operation 层（Operation.ReconcileDriven），由组合根注入。
/// <para>
/// 插点语义：<see cref="Invocation.InvocationKernel.ExecuteAsync"/> 是所有函数调用的唯一入口，
/// 因此「每次触发 → 执行函数 → 输出写回」在该处被本钩子包裹；实现方在包裹内开/收触发域信号账并裁决。
/// </para>
/// <item>
/// 未注入实现（null） ⇒ 框架直通，零开销、零回归。
/// </item>
/// </summary>
public interface IEventReconcileHook
{
    /// <summary>是否启用（false = 框架直通，不调用 <see cref="InvokeAsync"/>）。</summary>
    bool Enabled { get; }

    /// <summary>
    /// 绑定期登记「函数名 → 对账契约」：<see cref="EventBinding.Reconcile"/> 在装配控件绑定时经此登记，
    /// 供触发期按函数名解析契约。重复登记以最后一次为准。
    /// </summary>
    void RegisterContract(string functionName, ReconcileConfig contract);

    /// <summary>
    /// 包裹一次函数调用：实现方在调用前后开/收触发域信号账，并按契约裁决（无契约/已关闭则直通 <paramref name="invoke"/>）。
    /// strict 模式下失配应抛 <see cref="System.Exception"/>（由实现定义），warn 模式仅落事件。
    /// </summary>
    Task<object?> InvokeAsync(
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        Func<Task<object?>> invoke);
}
