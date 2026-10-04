#nullable enable

using System;
using System.Threading;

namespace Share.EventBindingOrchestrator.Invocation;

/// <summary>
/// 绑定到 AEF AsyncOperation 的上下文：心跳/取消/进程注册都落到操作上。
/// 使调度器/看门狗真正掌控底层进程，杜绝「框架判死后子进程仍在跑」的孤儿。
/// </summary>
public sealed class KernelContext : IInvocationContext
{
    private readonly Modules.Threading.AsyncOperation _op;

    public KernelContext(Modules.Threading.AsyncOperation op)
    {
        _op = PortGuard.Required(op, nameof(op));
    }

    public CancellationToken CancellationToken => _op.Token;

    public void ReportProgress() => _op.TouchHeartbeat();

    public void RegisterProcess(Modules.Threading.IProcessHandle process)
    {
        if (process != null) _op.Process = process;
    }

    public void RegisterProcess(System.Diagnostics.Process process)
        => RegisterProcess(process != null
            ? new Modules.Threading.ChildProcessHandle(process)
            : null!);
}

/// <summary>AEF 未装配时的兜底上下文：取消/心跳/进程均为空操作（尽力而为）。</summary>
public sealed class NullInvocationContext : IInvocationContext
{
    public CancellationToken CancellationToken => CancellationToken.None;

    public void ReportProgress() { }

    public void RegisterProcess(Modules.Threading.IProcessHandle process) { }

    public void RegisterProcess(System.Diagnostics.Process process) { }
}
