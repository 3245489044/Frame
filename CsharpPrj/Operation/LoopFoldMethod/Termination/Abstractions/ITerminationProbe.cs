namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 宿主事实探针（窄端口）：只回答「宿主是否已关闭/取消」，不反向引用宿主程序集。
/// 由 FileDriven / Agent 在组合根实现并注入。
/// </summary>
public interface ITerminationProbe
{
    /// <summary>宿主已关闭（关窗/进程退出请求）→ 应强制终止并产出 HostClosed 终态。</summary>
    bool IsHostClosed { get; }
}
