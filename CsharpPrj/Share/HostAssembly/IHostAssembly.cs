using System.Threading;
using System.Threading.Tasks;
using Share.HostAssembly.Abstractions;

namespace Share.HostAssembly;

/// <summary>
/// 宿主装配门面（组合根契约）—— 宿主只需调用一个方法并读取装配摘要，
/// 即可完成整个运行时的一次性装配。装配产物由宿主或实现类持有，生命周期跨多次调用存活。
/// </summary>
public interface IHostAssembly
{
    /// <summary>装配器标识（诊断/日志用）。</summary>
    string Name { get; }

    /// <summary>装配并启动宿主运行时；返回装配结果（含降级清单与失败归因）。</summary>
    Task<BootstrapResult> BootstrapAsync(CancellationToken ct = default);
}
