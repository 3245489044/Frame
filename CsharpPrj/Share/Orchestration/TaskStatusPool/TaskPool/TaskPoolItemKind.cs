namespace Share.TaskStatusPool.TaskPool
{
    /// <summary>
    /// 任务池条目语义类别（通用，零领域）：执行任务 / 信号暂存 / 纯簿记。
    /// <para>刷新与在途判定只消费 <see cref="Executable"/>；<see cref="Signal"/> / <see cref="Bookkeeping"/>
    /// 永不唤醒刷新心跳。</para>
    /// </summary>
    public enum TaskPoolItemKind
    {
        Executable,
        Signal,
        Bookkeeping,
    }
}
