using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Pipeline.StepExec
{
    /// <summary>
    /// 标准中间件工厂（当前为 I3 单步超时 + 步骤 invoke 观测）。每条缺省语义 = 直跑等价；
    /// 仅当对应策略（如 <see cref="StepExecContext.StepTimeout"/> &gt; 0）置位才生效。
    /// 观测中间件经注入的 <see cref="Action{T1,T2}"/> 日志插槽触达（框架零 Agent 日志依赖）。
    /// </summary>
    public static class StepExecMiddlewares
    {
        /// <summary>
        /// 步骤函数调用观测：把 invokeStart / funcReturned 两步诊断 + 计时下沉为唯一走廊上的正交中间件
        /// （同一 msgId/字段/时序 → 行为等价；保持「有 invokeStart 缺 funcReturned = 卡 handler 内部」的卡死诊断判定口径不变）。
        /// </summary>
        /// <param name="logKey">日志键前缀（如 "NodeExecutionService_ExecuteAsync"）；实际键 = logKey + "_invokeStart" / "_funcReturned"。</param>
        /// <param name="log">日志插槽（委托，Agent 侧回填日志门面）。</param>
        public static StepExecMiddleware Observability(string logKey, Action<string, object?> log)
        {
            return async (ctx, handler, next, ct) =>
            {
                var sw = Stopwatch.StartNew();
                log($"{logKey}_invokeStart", new { funcName = ctx.FuncName });
                var result = await next(ct).ConfigureAwait(false);
                sw.Stop();
                log($"{logKey}_funcReturned", new
                {
                    funcName = ctx.FuncName,
                    elapsedMs = sw.ElapsedMilliseconds,
                    isNull = result is null,
                    resultType = result?.GetType().Name,
                });
                return result;
            };
        }

        /// <summary>
        /// 单步超时（I3：函数级卡死自愈/自证的前提）。ctx.StepTimeout &gt; 0 → per-step linked-CTS +
        /// <see cref="CancellationTokenSource.CancelAfter"/>，经 <paramref name="next"/> 注入的被取代 token 真实传导到末端 handler。
        /// 外层 ct 取消 → 上抛（不记 TO）；仅本步超时（非外层取消）→ 上抛 OperationCanceledException，由调用方/引擎按超时语义归类。
        /// ctx.StepTimeout 为空/0 → 纯透传（默认行为等价，可回退关闭，防误杀真实长轮步）。
        /// </summary>
        public static StepExecMiddleware StepTimeout()
        {
            return async (ctx, handler, next, ct) =>
            {
                if (ctx.StepTimeout is not { } timeout || timeout <= TimeSpan.Zero)
                    return await next(ct).ConfigureAwait(false);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);
                try
                {
                    // 把 per-step token 注入余下链路（末端 handler 收到它），使取消真实作用于在途函数调用。
                    return await next(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // 本步超时（非外层取消）：取消已到达在途调用；上抛由引擎按超时语义记 TO/收敛，不在此归类。
                    throw;
                }
            };
        }
    }
}