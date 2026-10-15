using System;

namespace Strategy.Refresh.Ports
{
    /// <summary>
    /// 定时器工厂端口（L1，通用）：把「真实定时器」与「测试替身」统一为一个可注入面，
    /// 使宿主 / 单测可在不依赖真实时钟的前提下驱动节拍。
    /// <para><b>【预留】</b>预定消费方：单测替身（<c>RefreshServiceTests.NoopClock</c>，已在用）与宿主自定义定时器注入。</para>
    /// </summary>
    public interface IRefreshClock
    {
        /// <summary>按 <paramref name="interval"/> 周期调度 <paramref name="callback"/>；返回可释放句柄。</summary>
        IDisposable Schedule(TimeSpan interval, Action callback);
    }

    /// <summary><see cref="IRefreshClock"/> 的真实实现：<see cref="System.Threading.Timer"/> 周期触发。</summary>
    public sealed class SystemRefreshClock : IRefreshClock
    {
        /// <inheritdoc />
        public IDisposable Schedule(TimeSpan interval, Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return new System.Threading.Timer(_ => callback(), null, interval, interval);
        }
    }
}
