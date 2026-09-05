#nullable enable

using System;
using FrontEndDriven.Pipeline;
using FrontEndDriven.Shared.Framework;
using Share.StateManagement;

namespace FrontEndDriven.Gate;

/// <summary>
/// 渲染事务（M2·D3 修复）：Load→Creation→Render 原子 + 异常补偿。
/// 语义：using 作用域内执行事务体；显式 <see cref="Commit"/> 落定；作用域结束未 Commit
/// （异常路径）→ Rollback = 仅销毁该 scope 残留实体（DestroyScopeEntities，绝不 Dispose composition）。
/// 状态机 Failed 态不由本类落定——ScopeLifecycleStateMachine 捕获 renderDelegate 异常自动落 Failed（E1），
/// 本类只保证 ECS 世界无残留实体（「失败场景无残留」验收）。
/// 提交/补偿骨架收敛到通用 <see cref="TransactionGuard"/>。
/// </summary>
public sealed class RenderTransaction : IDisposable
{
    private readonly string _scope;
    private readonly RenderPipelineComposition _composition;
    private readonly ICompositionLifecycle? _compositionLifecycle;
    private readonly TransactionGuard _guard;

    public RenderTransaction(
        string scope,
        RenderPipelineComposition composition,
        ICompositionLifecycle? compositionLifecycle)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("作用域不能为空", nameof(scope));
        _scope = scope;
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
        _compositionLifecycle = compositionLifecycle;

        // 事务回滚 = DestroyScopeEntities 级清理（含宿主清理/MountState 回写），
        // 与正常销毁同一出口（CompositionLifecycleAdapter.ScopeDestroyed），不发布 Destroying/Destroyed 事件
        // （事务失败非正常销毁语义）。
        _guard = new TransactionGuard(() =>
        {
            if (_compositionLifecycle != null)
                _compositionLifecycle.ScopeDestroyed(_scope);
            else
                _composition.DestroyScopeEntities(_scope);
        });
    }

    /// <summary>事务成功落定：跳过 Dispose 时的回滚补偿。</summary>
    public void Commit() => _guard.Commit();

    /// <summary>异常补偿（未 Commit 时触发）：回滚仅销毁本 scope 残留实体（StateCenter 单一路径，绝不 Dispose composition）。</summary>
    public void Dispose() => _guard.Dispose();
}
