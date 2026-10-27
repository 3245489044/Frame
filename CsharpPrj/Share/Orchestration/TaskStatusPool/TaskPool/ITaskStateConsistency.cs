namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 状态载体一致性探针（B3a）：写入前由持久化通道调用；分叉即 fail-fast（不写坏快照）。
/// <para>实现方 = 任务快照；判据载体 = 内存中多个状态镜像的结构事实（非执行者叙述）。</para>
/// </summary>
public interface ITaskStateConsistency
{
    /// <summary>存在状态分叉 ⇒ true 并给出可复算明细；一致 ⇒ false。</summary>
    bool TryGetStateDivergence(out string detail);
}
