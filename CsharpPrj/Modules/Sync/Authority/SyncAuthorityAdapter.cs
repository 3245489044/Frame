using Modules.Sync.Contracts;

namespace Modules.Sync.Authority;

/// <summary>
/// <see cref="ISyncAuthority"/> 的薄适配：只做三处转发，不放任何策略。
/// <list type="bullet">
/// <item><see cref="AdvanceOneTick"/> → 推进模拟 tick 直到跨过<b>恰好一个</b>决策边界；</item>
/// <item><see cref="ApplyInput"/> → 按 <see cref="TakeoverKind"/> 转成 <c>SubmitGuard</c> / <c>SubmitOvercharge</c>；</item>
/// <item><see cref="BuildSnapshot"/> → <c>BuildProjection</c> + <c>StateHash.Compute</c>。</item>
/// </list>
/// <para>
/// 命令的窗口校验与 <c>(TargetTick, CommandId)</c> 幂等由会话（SY-10）完成；
/// 本适配不做二次校验、不做预测、不产生拒绝原因。
/// </para>
/// </summary>
public sealed class SyncAuthorityAdapter : ISyncAuthority
{
    private readonly AuthorityTickLoop _loop;

    /// <summary>包装一个决策循环实例（同一实例只允许单线程驱动）。</summary>
    public SyncAuthorityAdapter(AuthorityTickLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        _loop = loop;
    }

    /// <inheritdoc />
    public long DecisionTick => _loop.DecisionTick;

    /// <inheritdoc />
    public void AdvanceOneTick()
    {
        // 契约频率下每 2 个模拟 tick 恰好产生 1 个决策边界；上限按 1 秒模拟帧数兜底（正常永不触发）。
        for (int i = 0; i < SyncProtocol.SimHz; i++)
        {
            if (_loop.AdvanceSimTick())
            {
                return;
            }
        }

        throw new InvalidOperationException("tick 时钟在 1 秒模拟帧内未产生决策边界。");
    }

    /// <inheritdoc />
    public void ApplyInput(TakeoverCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        switch (command.Kind)
        {
            case TakeoverKind.Guard:
                _loop.SubmitGuard(command.TargetTick);
                return;

            case TakeoverKind.Overcharge:
                // 契约命令不带槽位，槽位由权威端在应用 tick 确定性裁决
                _loop.SubmitOvercharge(command.TargetTick);
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "未知接管种类。");
        }
    }

    /// <inheritdoc />
    public Snapshot BuildSnapshot()
    {
        return _loop.BuildSnapshot();
    }
}
