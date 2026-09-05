
#nullable enable

using ECS.Core;
using BackEndDriven.Diagnostics;
using Modules.ComponentData.Extensions;
using Modules.Threading;
using System.Reactive.Linq;

namespace BackEndDriven.CommandExecution;

/// <summary>
/// await 命令（响应式等待，非轮询）：订阅 ComponentDataStore 上目标控件键（control:{name}）的变更流，
/// 事件驱动地阻塞（命令在后台/非 UI 线程执行，与 SleepSystem 同线程模型）直到发布非空值或超时。
/// 用于替代「固定 sleep + 提前 get」——等待异步函数（如真实 AI 调用）将结果写回控件后继续。
/// 发布方：EcsStateAccessor.SetValue → ComponentDataStore.Set("control.{name}", value)。
/// 语法: await &lt;controlName&gt; [timeout=&lt;ms&gt;]（默认 60000）。
/// </summary>
public class AwaitValueSystem : CommandSystemBase, IAsyncCommandSystem
{
    public const string ControlKeyPrefix = "control.";

    public const int DefaultTimeoutMs = 60_000;

    public AwaitValueSystem(EntityManager entityManager)
        : base(entityManager, "await", "waitfor")
    {
    }

    /// <summary>同步路径（legacy Update 管线）委托异步实现；主脚本路径走非阻塞异步。</summary>
    public override void Execute(Entity entity, CommandMetaComponent meta)
        => ExecuteAsync(entity, meta).GetAwaiter().GetResult();

    /// <summary>
    /// 异步命令实现（M1/M2·非阻塞）：把「等待目标控件键发布非空值」建模为 <see cref="AsyncOperation{T}"/>，
    /// 经 <see cref="OperationExtensions.TryWaitAsync"/>（Task.WaitAsync，非阻塞）订阅完成信号——
    /// 命令线程不再 <c>tcs.Task.Wait(timeout)</c> 同步阻塞，慢异步函数写回期间让出线程。
    /// </summary>
    public async Task ExecuteAsync(Entity entity, CommandMetaComponent meta)
    {
        var target = meta.Target;
        if (string.IsNullOrWhiteSpace(target))
        {
            SetFailure(entity, "await: 缺少目标控件名");
            return;
        }

        var timeoutMs = ResolveTimeout(meta);
        var key = ControlKeyPrefix + target;

        // M2·AEF 收编：完成信号经 AsyncOp 单一终结（op.Complete 于发布非空值时触发），替代手建 TCS + Wait()。
        using var op = new AsyncOperation<string>($"await:{target}");

        // 事件驱动：订阅目标键变更流，发布非空值即 op.Complete（非轮询）。
        using var subscription = EntityManager.GetComponentDataStore()
            .DataChanged
            .Where(e => string.Equals(e.Key, key, StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(e.NewValue?.ToString()))
            .Subscribe(_ => op.Complete(target));

        // 非阻塞等待发布；timeoutMs <= 0 → 不设总时限（依赖发布方自身空闲看门狗自终止）。
        var completed = await op.Completion.TryWaitAsync(
            timeoutMs > 0 ? TimeSpan.FromMilliseconds(timeoutMs) : Timeout.InfiniteTimeSpan)
            .ConfigureAwait(false);

        if (completed)
        {
            BackEndLog.Log("Command_Await_Success", new { target, key });
            SetSuccess(entity, target);
        }
        else
        {
            BackEndLog.Log("Command_Await_Timeout", new { target, key, timeoutMs });
            SetFailure(entity, $"await '{target}' 超时（{timeoutMs}ms）: 未收到 '{key}' 的非空发布");
        }
    }

    private static int ResolveTimeout(CommandMetaComponent meta)
    {
        if (meta.Parameters != null
            && meta.Parameters.TryGetValue("timeout", out var v)
            && v != null
            && int.TryParse(v.ToString(), out var parsed))
        {
            return parsed;   // parsed <= 0 → 不设总时限（无限等待，依赖发布方自身空闲看门狗自终止）
        }
        return DefaultTimeoutMs;
    }
}
