#nullable enable

using System;

namespace FrontEndDriven.Shared.Framework;

/// <summary>
/// 事务守卫（通用框架）：using 作用域内执行事务体，显式 <see cref="Commit"/> 落定；
/// 作用域结束未 Commit（异常路径）→ 执行补偿 <see cref="_onCompensate"/>。
/// 收敛 RenderTransaction 等"提交/补偿"同构（与 C# TransactionScope 的 Complete 语义对齐）。
/// </summary>
public sealed class TransactionGuard : IDisposable
{
    private readonly Action _onCompensate;
    private bool _completed;

    /// <param name="onCompensate">未提交即释放时执行的补偿动作。</param>
    public TransactionGuard(Action onCompensate)
    {
        _onCompensate = onCompensate ?? throw new ArgumentNullException(nameof(onCompensate));
    }

    /// <summary>事务成功落定：跳过 Dispose 时的补偿。</summary>
    public void Commit() => _completed = true;

    /// <summary>异常补偿（未 Commit 时触发）：仅执行一次补偿动作。</summary>
    public void Dispose()
    {
        if (_completed)
            return;
        _completed = true;
        _onCompensate();
    }
}
