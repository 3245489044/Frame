namespace Operation.StateBlock;

/// <summary>
/// daemon 正常返回业务错误（含内层任务超时/结果丢失）——属于任务最终结果，不重试，映射为业务失败。
/// 区别于基建故障（进程挂起/IPC 断/池未就绪），后者由池内补位重试承担。
/// </summary>
public class DaemonPoolException : Exception
{
    public string Cmd { get; }

    public DaemonPoolException(string cmd, string? error)
        : base(string.IsNullOrWhiteSpace(error) ? $"daemon 命令 '{cmd}' 失败" : error)
    {
        Cmd = cmd;
    }
}
