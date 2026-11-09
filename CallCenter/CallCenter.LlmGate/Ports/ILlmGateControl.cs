using CallCenter.LlmGate.Model;

namespace CallCenter.LlmGate.Ports;

/// <summary>控制面（写）。实现方：同进程 GateCore 或跨进程 HTTP 客户端。</summary>
public interface ILlmGateControl
{
    string Status();
    string SetMode(string mode);
    string Halt(string? reason);
    string Resume();
    bool TryApprove(string callId, out string message);
    bool TryDeny(string callId, string? reason, out string message);
}

/// <summary>读面（只读快照）。</summary>
public interface ILlmGateQuery
{
    GateStats SnapshotStats();
    IReadOnlyList<LlmCallSpan> SnapshotCalls(string? filter, int take);
    IReadOnlyList<PendingView> SnapshotPending();
    LlmCallSpan? Find(string callId);
}
