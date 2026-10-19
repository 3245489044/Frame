using Modules.Threading;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 强制退出出口（窄端口）：宿主在此做「不等待被取消方返回」的收尾动作
/// （释放门/租约、标记终态、结构化失败上报）。禁止在此等待业务任务完成。
/// </summary>
public interface ITerminationSink
{
    void OnForcedExit(TerminationRequest request);
}
