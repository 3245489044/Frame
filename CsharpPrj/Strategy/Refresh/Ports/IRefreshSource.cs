using Strategy.Refresh.Model;

namespace Strategy.Refresh.Ports
{
    /// <summary>
    /// 刷新源（L1 契约，通用）：宿主 / UI 侧声明一个"变化会引发刷新"的来源（如任务池状态、数据表、兜底轮询）。
    /// <para>统一由 <see cref="RefreshScheduler"/> 登记与管理——节拍 / 去重 / 单飞 / 重试 / 优先级全部在框架侧，
    /// 源只需上报变更范围（scope 键；null/空白 = 全量）与优先级，**不再各自维护节拍与去重**。</para>
    /// </summary>
    public interface IRefreshSource
    {
        /// <summary>源名（诊断 / 去重登记用；进程内唯一）。</summary>
        string Name { get; }

        /// <summary>源优先级（缺省 <see cref="RefreshPriority.High"/>；兜底轮询类源应置 <see cref="RefreshPriority.Low"/>）。</summary>
        RefreshPriority Priority => RefreshPriority.High;

        /// <summary>变化通知：参数为变更范围键（null/空白 = 显式全量）。</summary>
        event System.Action<string?>? Changed;

        /// <summary>启动源（订阅底层事件）。须幂等。</summary>
        void Start();

        /// <summary>停止源（退订底层事件）。须幂等。</summary>
        void Stop();
    }
}


