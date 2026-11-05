using System.ComponentModel;
using System.Diagnostics;

namespace Modules.Threading;

/// <summary>
/// 进程句柄抽象：供框架在取消/空闲超时回调中终止底层孤儿进程。
/// </summary>
public interface IProcessHandle
{
    int Id { get; }

    /// <summary>终止进程（默认连进程树）。</summary>
    void Kill(bool entireProcessTree = true);

    bool IsExited { get; }
}

/// <summary>包装 <see cref="System.Diagnostics.Process"/>，Dispose 时确保退出。</summary>
public sealed class ChildProcessHandle : IProcessHandle, IDisposable
{
    private readonly Process _process;

    public ChildProcessHandle(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        _process = process;
    }

    public int Id
    {
        get
        {
            try { return _process.Id; }
            catch { return -1; }
        }
    }

    public bool IsExited
    {
        get
        {
            try { return _process.HasExited; }
            catch (InvalidOperationException) { return true; }
            catch (Win32Exception) { return true; }
        }
    }

    public void Kill(bool entireProcessTree = true)
    {
        if (IsExited) return;
        try { _process.Kill(entireProcessTree); }
        catch (InvalidOperationException) { /* 已退出 */ }
        catch (Win32Exception) { /* 权限/句柄无效 */ }
    }

    public void Dispose() => Kill(entireProcessTree: true);
}
