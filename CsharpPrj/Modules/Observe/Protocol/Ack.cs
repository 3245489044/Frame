namespace Modules.Observe;

/// <summary>
/// 回执：Consumer(B) → Producer(A)。<c>Level</c> 表示推进到的 ack 级别；<c>Converged</c> 级别的 ack 必须携带
/// 可校验证据（见 <see cref="ConvergedProof"/>），防止「口头声称收敛」。
/// </summary>
public sealed record Ack(string ChangeId, AckLevel Level, object? Evidence) : Message(ChangeId);
