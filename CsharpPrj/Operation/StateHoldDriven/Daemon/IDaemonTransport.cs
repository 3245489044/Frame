namespace Operation.StateBlock;

/// <summary>
/// 传输策略 —— 通用 daemon 池经它获取 worker 宿主与预热请求，不感知具体工具。
/// 具体工具（Cline）以本接口实现自身 daemon 的 spawn/预热语义。
/// </summary>
public interface IDaemonTransport
{
    /// <summary>创建单个 worker 宿主（尚未 start）；spawn/阶段追踪/就绪语义由宿主承载。</summary>
    IWorkerHost CreateHost(string workerId);

    /// <summary>构造预热请求（使 worker 达到就绪态）。预算参数供内层自报超时使用。</summary>
    DaemonRequest CreateWarmRequest(IWorkerHost host, TimeSpan budget);
}
