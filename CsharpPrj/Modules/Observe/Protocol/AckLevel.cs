namespace Modules.Observe;

/// <summary>三级 ack 语义：Accepted=命令已到达传输层并被接纳；Applied=B 内部模型已更新；Converged=B 可观测投影已就绪（最终保证）。</summary>
public enum AckLevel
{
    Accepted,
    Applied,
    Converged
}
