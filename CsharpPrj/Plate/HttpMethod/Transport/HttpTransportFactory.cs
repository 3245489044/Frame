using System;
using System.Collections.Concurrent;
using System.Net.Http;

namespace Plate.HttpMethod.Transport
{
    /// <summary>
    /// 受管 HttpClient 连接池工厂（SingleImplementation / TH-12）—— 进程级唯一受管连接池。
    /// 收敛散落的静态 <c>HttpClient{Timeout=60s}</c> 为单一 <c>SocketsHttpHandler</c> 连接池：
    /// 连接复用 + 池寿命（<see cref="HttpTransportOptions.PooledConnectionLifetime"/>）防 socket 耗尽 / DNS 漂移。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><see cref="Create(HttpTransportOptions?)"/>：按"选项指纹"缓存进程级单例；相同配置 → 复用同一池，不同配置 → 独立池。返回实例为进程级共享，<b>不应 Dispose</b>（进程退出即回收）。</item>
    ///   <item><see cref="CreateInstance(HttpTransportOptions?)"/>：每次新建独立受管池，由调用方自行 Dispose（生命周期自管理消费方用，如 <c>HttpBarSource</c>）。</item>
    /// </list>
    /// <para><b>超时治理</b>：本工厂不设硬编码单点超时（<see cref="HttpClient.Timeout"/> = Infinite），改由调用方显式施加（CancelAfter / Polly）。</para>
    /// </remarks>
    public static class HttpTransportFactory
    {
        private static readonly ConcurrentDictionary<string, HttpClient> Pools = new(StringComparer.Ordinal);

        /// <summary>按配置获取进程级共享连接池单例（相同配置 → 同一池）。进程级共享，无需 Dispose。</summary>
        public static HttpClient Create(HttpTransportOptions? options = null)
        {
            options ??= new HttpTransportOptions();
            var key = BuildKey(options);
            return Pools.GetOrAdd(key, _ => Build(options));
        }

        /// <summary>新建一个独立受管连接池实例（调用方自行 Dispose）。与 <see cref="Create"/> 互不共享。</summary>
        public static HttpClient CreateInstance(HttpTransportOptions? options = null)
            => Build(options ?? new HttpTransportOptions());

        private static string BuildKey(HttpTransportOptions o)
            => $"{o.PooledConnectionLifetime.Ticks}|{o.PooledConnectionIdleTimeout.Ticks}|{o.MaxConnectionsPerServer}|{o.AllowAutoRedirect}|{o.AutomaticDecompression}";

        private static HttpClient Build(HttpTransportOptions o)
        {
            var handler = new SocketsHttpHandler
            {
                // 连接池治理：连接复用防 socket 耗尽；池寿命防 DNS/连接漂移；空闲超时回收闲置连接。
                PooledConnectionIdleTimeout = o.PooledConnectionIdleTimeout,
                PooledConnectionLifetime = o.PooledConnectionLifetime,
                MaxConnectionsPerServer = o.MaxConnectionsPerServer,
                AllowAutoRedirect = o.AllowAutoRedirect,
                AutomaticDecompression = o.AutomaticDecompression,
            };
            return new HttpClient(handler)
            {
                // 不设硬编码单点超时：由调用方（网关看门狗 / Polly）显式施加，避免与业务预算双源漂移。
                Timeout = Timeout.InfiniteTimeSpan,
            };
        }
    }
}
