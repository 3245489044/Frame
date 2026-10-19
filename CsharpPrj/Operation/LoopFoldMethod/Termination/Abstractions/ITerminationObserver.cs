using System;
using Modules.Threading;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 终止观测端口（可选）：心跳 / 裁决 / 强制退出三事件，供日志、遥测、死信落库。
/// 实现须非阻塞；事件不改变裁决结果（观测面与裁决面分离）。
/// </summary>
public interface ITerminationObserver
{
    /// <summary>真实推进心跳（progressPoint 为推进点标签）。</summary>
    void OnBeat(string scopeId, string progressPoint);

    /// <summary>产生终值裁决（尚未执行强制退出）。</summary>
    void OnVerdict(TerminationVerdict verdict);

    /// <summary>强制退出已触发（结构化证据）。</summary>
    void OnTerminated(TerminationRequest request);
}
