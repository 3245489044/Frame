using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Threading
{
    /// <summary>
    /// 后台 worker 的执行边界（StateBus 治理 P3）：进入 TaskPool / Outbox / 任务池消费者首帧时，
    /// 以『**切断外层 ExecutionContext 流动**』启动工作，使外层 ambient（AsyncLocal）值**不**被隐式带过边界，
    /// 避免把本不该共享的 run 上下文/令牌/预算泄漏到后台线程造成双写或错读。
    /// <para>
    /// 原理：在 <see cref="ExecutionContext.SuppressFlow"/> 下排布到线程池，排布点不捕获调用方 ExecutionContext
    /// → worker 运行于**空上下文**，读取各 ambient 槽得到默认值（null）。需要传递的最小句柄须由调用方显式传入
    /// （在 worker 体首帧用对应 <see cref="AmbientSlot{T}"/>.Enter 具名写入），即『显式边界显式化』。
    /// </para>
    /// </summary>
    public static class FlowBoundary
    {
        /// <summary>在切断外层上下文的线程池工作中执行异步体（无返回）。</summary>
        public static Task RunAsync(Func<Task> body)
        {
            ArgumentNullException.ThrowIfNull(body);
            using (ExecutionContext.SuppressFlow())
            {
                return Task.Run(body);
            }
        }

        /// <summary>在切断外层上下文的线程池工作中执行异步体（带返回）。</summary>
        public static Task<T> RunAsync<T>(Func<Task<T>> body)
        {
            ArgumentNullException.ThrowIfNull(body);
            using (ExecutionContext.SuppressFlow())
            {
                return Task.Run(body);
            }
        }

        /// <summary>在切断外层上下文的线程池工作中执行同步体。</summary>
        public static Task Run(Action body)
        {
            ArgumentNullException.ThrowIfNull(body);
            using (ExecutionContext.SuppressFlow())
            {
                return Task.Run(body);
            }
        }
    }
}
