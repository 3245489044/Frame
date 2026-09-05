using System;
using System.Net;

namespace Plate.HttpMethod.Transport
{
    /// <summary>
    /// 受管 HTTP 连接池传输配置（<see cref="HttpTransportFactory"/> 消费）。
    /// <para><b>超时治理</b>：本配置不设硬编码单点超时（<see cref="HttpClient.Timeout"/> = Infinite），
    /// 改由调用方显式施加（<c>CancellationTokenSource.CancelAfter</c> / Polly <c>RetryPolicy</c>/<c>TimeoutPolicy</c>）。</para>
    /// </summary>
    public sealed class HttpTransportOptions
    {
        /// <summary>连接池寿命（超时即关闭旧连接并重建，避免 DNS/目标侧 IP 漂移）。默认 5min。</summary>
        public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>连接池空闲保活时长。默认 60s。</summary>
        public TimeSpan PooledConnectionIdleTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>到同一服务器的最大连接数。默认 16。</summary>
        public int MaxConnectionsPerServer { get; set; } = 16;

        /// <summary>是否跟随重定向。</summary>
        public bool AllowAutoRedirect { get; set; }

        /// <summary>自动解压方式（GZip/Deflate）。默认 <see cref="DecompressionMethods.None"/>（调用方按需开启，如 HttpBarSource 需 GZip/Deflate）。</summary>
        public DecompressionMethods AutomaticDecompression { get; set; } = DecompressionMethods.None;
    }
}
