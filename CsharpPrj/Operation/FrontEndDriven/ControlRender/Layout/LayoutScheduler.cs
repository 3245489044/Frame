#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.Shared.Framework;
using TConfig.Abstractions;

namespace FrontEndDriven.ControlRender.Layout;

/// <summary>
/// 布局反应式调度器（LayoutScheduler）：以 scope 为键的 latest-wins 合并调度，UI 线程单消费者。
/// 收敛 RenderRequestQueue 的 scope+version 去重模式：同 scope 尺寸变化风暴合并为最终一次重算，
/// 期间新事件仅更新版本号（旧版本消费时经 TryConsumeIfLatest 判定过期而丢弃）。
/// </summary>
public sealed class LayoutScheduler
{
    private readonly Func<IUIDispatcher?> _dispatcherResolver;
    private readonly Action<string> _work;
    private readonly VersionedKeyedQueue<string, string> _pending = new(StringComparer.OrdinalIgnoreCase);

    // 预算耗尽重排判定（由 LayoutSubsystem 收敛状态机注入）：仅 Retrying 可重试，Blocked/Converged/Abandoned 不重排。
    // null（测试态独立构造）→ 一律视为可重试（默认 true），保留「不静默滞留」护栏。
    private readonly Func<string, bool>? _shouldRetry;

    // 终态隔离回调（LayoutSubsystem 注入）：scope 预算耗尽超限时通知收敛机置 Abandoned 并退订宿主几何，
    // 从触发源上掐断「无法物理收敛 scope」跨 drain 反复重新入队的空转。
    private readonly Action<string>? _onBudgetExhausted;

    // 收敛阈值单源：MaxDrainPasses / ReflowStormThreshold / RetryBudget 定义见
    // LayoutConvergencePolicy（确定性收敛状态机的阈值单一权威），本处不再散落硬编码。

    // 风暴跟踪状态（LayoutScheduler 为 UI 线程单消费者，仅 DrainAsync 内访问，无需加锁）。
    private string? _lastReflowScope;
    private int _consecutiveReflowCount;

    // 预算耗尽计费按 scope 计（per-scope 隔离）：单条坏 scope 不再毒化全局重排——
    // 每 scope 独立累计「连续预算耗尽仍滞留待重算」次数；仅队列整体清空时清空，
    // 健康 scope 一旦收敛即随队列清空复位，不积累误判。
    private readonly ConcurrentDictionary<string, int> _budgetExhaustions = new(StringComparer.OrdinalIgnoreCase);

    // 防重入守卫（独立于 VersionedKeyedQueue 内部 _draining）：`_work` 内可能再 Schedule（如 Reflow→RequestReflow），
    // 同步路径下会重入 DrainAsync。若嵌套 DrainAsync 在 DrainAllAsync 的重入守卫下"不消费"却仍执行预算/隔离逻辑，
    // 会误隔离健康 scope（压力测实测）——故顶层 drain 才允许跑预算隔离，嵌套调用直接返回，由外层 while 吞新注册。
    private int _draining;

    public LayoutScheduler(
        Func<IUIDispatcher?> dispatcherResolver,
        Action<string> work,
        Func<string, bool>? shouldRetry = null,
        Action<string>? onBudgetExhausted = null)
    {
        _dispatcherResolver = dispatcherResolver ?? throw new ArgumentNullException(nameof(dispatcherResolver));
        _work = work ?? throw new ArgumentNullException(nameof(work));
        _shouldRetry = shouldRetry;
        _onBudgetExhausted = onBudgetExhausted;
    }

    /// <summary>待重算作用域数（诊断用）。</summary>
    public int PendingScopeCount => _pending.Count;

    /// <summary>入队一次作用域重算（同 scope 旧版本自动失效，latest-wins）。</summary>
    public void Schedule(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return;

        _pending.Register(scope, scope);
        ScheduleDrain();
    }

    private void ScheduleDrain()
    {
        var dispatcher = _dispatcherResolver();
        if (dispatcher == null)
        {
            // 调度器未注册（测试/装配期）→ 同步 Drain（单消费者在调用线程直接执行，保持同步语义）。
            _ = DrainAsync();
            return;
        }

        dispatcher.Post(() => _ = DrainAsync());
    }

    /// <summary>
    /// 单消费者：对每个有待重算作用域仅执行最新版本（过期丢弃），执行后出队。
    /// drain 循环骨架（重入守卫 / 快照 / 版本去重 / 预算 / try/finally / while 吞执行期新请求）收敛到
    /// <see cref="VersionedKeyedQueue{TKey,TEntry}.DrainAllAsync"/>；本方法仅提供最新版本执行的 work 委托，
    /// 并在其中做「重排风暴护栏」（同一 scope 连续重排超阈值 → 记告警并返回 false = break，与原实现逐行一致的终止语义）。
    /// 预算用尽仍有剩余 → 按 scope 独立计费，仍在退避预算内（<see cref="LayoutConvergencePolicy.RetryBudget"/>）的可重试
    /// scope 有限次重新调度消化剩余；超限或无可重试的 scope 终态隔离（移出待重算 + 通知收敛机 Abandoned）。
    /// </summary>
    private async Task DrainAsync()
    {
        // 防重入：drain 进行中再触发 ScheduleDrain → 嵌套 DrainAsync 直接返回。
        // 由外层 drain 的 DrainAllAsync `while(_latest.Count>0)` 循环吞掉执行期新注册，确保不重复消费；
        // 同时杜绝「嵌套 drain 在重入守卫下未消费却仍执行预算/隔离」误伤健康 scope。
        if (Interlocked.Exchange(ref _draining, 1) == 1)
            return;

        try
        {
            _lastReflowScope = null;
            _consecutiveReflowCount = 0;

            await _pending.DrainAllAsync(
                execute: (scope, _, version) =>
                {
                    // 重排风暴护栏：单次 Drain 内同一 scope 连续重排超过阈值 → 记告警并终止本 pass 剩余遍历，
                    // 避免 `_work` 内对同一 scope 反复 Schedule 造成 ScheduleDrain 重入无限风暴。
                    // （正常路径下一 scope 的 reflow 不会无界重排自身；此处仅护栏，不吞正确任务——
                    //  风暴 scope 已被 DrainAllAsync 经 TryConsumeIfLatest 出队且未执行 _work，不会被再次入队。）
                    if (scope == _lastReflowScope)
                        _consecutiveReflowCount++;
                    else
                    {
                        _lastReflowScope = scope;
                        _consecutiveReflowCount = 1;
                    }

                    if (_consecutiveReflowCount > LayoutConvergencePolicy.ReflowStormThreshold)
                    {
                        FrontEndDrivenLog.Log("LayoutScheduler_reflow_storm",
                            new { scope, consecutive = _consecutiveReflowCount });
                        return Task.FromResult(false); // = 原 `break`：终止本 pass 剩余遍历，交由外层 while 重新判定
                    }

                    try
                    {
                        _work(scope);
                    }
                    catch (Exception ex)
                    {
                        FrontEndDrivenLog.Log("LayoutScheduler_work_failed", new { scope, error = ex.Message });
                    }
                    return Task.FromResult(true);
                },
                maxPasses: QueueDrainPolicy.MaxPassesPerDrain);
        }
        finally
        {
            // 消费阶段结束即释放守卫，使预算逻辑尾部的 ScheduleDrain（同步路径）可正常再入。
            Interlocked.Exchange(ref _draining, 0);
        }

        if (_pending.Count == 0)
        {
            // 本 drain 已清空（scope 全部收敛）→ 复位全部预算耗尽计数（per-scope），
            // 保证健康 scope 收敛后计数归零、不积累误判。
            _budgetExhaustions.Clear();
            return;
        }

        FrontEndDrivenLog.Log("LayoutScheduler_drain_budget_exhausted",
            new { pendingCount = _pending.Count });

        // 四态化隔离（per-scope）：预算耗尽计费按 scope 计——单条坏 scope 不再毒化全局重排。
        // 对每个「执行 _work 后仍被重新入队、未物理收敛」的 scope：
        //   ① 递增其独立预算耗尽计数；
        //   ② 超过 RetryBudget → 终态隔离：通知收敛机（_onBudgetExhausted → Abandoned + 退订宿主几何，
        //      掐断后续重新入队），并移出待重算，不再重排（杜绝不可收敛 scope 无限自激）；
        //   ③ 未超限但状态为 Blocked/Converged/Abandoned（无可重试）→ 移出待重算，不静默滞留。
        // 预算计数仅队列整体清空时复位（见下方 final），健康 scope 收敛即复位，不积累误判。
        var remaining = _pending.Snapshot();
        bool anyReschedulable = false;
        foreach (var (key, _, _) in remaining)
        {
            int count = _budgetExhaustions.TryGetValue(key, out var prev) ? prev + 1 : 1;
            bool retryable = _shouldRetry?.Invoke(key) ?? true;

            if (!retryable || count > LayoutConvergencePolicy.RetryBudget)
            {
                // 终态：从待重算移除（scope 不应长期滞留）；超限且可重试 → 通知收敛机隔离。
                _pending.TryRemove(key);
                _budgetExhaustions.TryRemove(key, out _);
                if (retryable)
                    _onBudgetExhausted?.Invoke(key);
                FrontEndDrivenLog.Log("LayoutScheduler_drain_reschedule_giveup",
                    new
                    {
                        scope = key,
                        pendingCount = _pending.Count,
                        consecutive = count,
                        note = retryable ? "连续预算耗尽超上限，已终态隔离" : "剩余 scope 无可重试（Blocked/Converged/Abandoned）"
                    });
                continue;
            }

            _budgetExhaustions[key] = count;
            anyReschedulable = true; // 仍在退避预算内的可重试 scope → 消化剩余
        }

        if (anyReschedulable && _pending.Count > 0)
            ScheduleDrain();
    }
}
