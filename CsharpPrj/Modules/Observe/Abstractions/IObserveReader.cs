using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Observe;

/// <summary>
/// 读方观测接口（应用层读方唯一入口）。<c>ReadAsync</c> 读取 B 的可观测投影；<paramref name="minVersion"/> 让陈旧读取可被
/// 检测并等待（I2 版本单调）：读到的版本低于 <paramref name="minVersion"/> 时 <see cref="Observed{T}.IsStale"/> 为 true。
/// 传输不支持推送式证据时，实现内部退化为 Probe 轮询（同一接口、两套实现）。
/// </summary>
public interface IObserveReader<T>
{
    Task<Observed<T>> ReadAsync(string key, long minVersion = 0, CancellationToken ct = default);
}
