using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Share.Composition.AssemblyFramework;

/// <summary>破坏性批量动作的交互态（选择集 + 二次确认待决 + 最近结果）。</summary>
public interface IDestructiveBatchState
{
    /// <summary>当前选择集（行身份条目）。</summary>
    IReadOnlyCollection<string> Selection { get; }

    /// <summary>待二次确认的命令（null=无）。</summary>
    string? PendingCommand { get; set; }

    /// <summary>待二次确认的键集快照。</summary>
    string[] PendingKeys { get; set; }

    /// <summary>最近一次结果（可见化文案）。</summary>
    string? Result { get; set; }

    /// <summary>清空选择集。</summary>
    void ClearSelection();
}

/// <summary>破坏性批量动作的执行结论。</summary>
public enum DestructiveBatchDecision
{
    /// <summary>空选择集，已拒绝。</summary>
    RejectedEmpty,

    /// <summary>首次点击，已置为待二次确认。</summary>
    AwaitConfirm,

    /// <summary>二次确认通过，动作已执行。</summary>
    Executed,
}

/// <summary>
/// 破坏性批量动作统一骨架 —— 「空集拒绝 → 连续两次同命令确认（键集一致）→ 执行 → 清选择集」。
/// <para>
/// 通用件（零领域类型）：命令名、空集文案、确认文案、动作体均由调用方注入；
/// 状态读写经 <see cref="IDestructiveBatchState"/>。
/// </para>
/// </summary>
public static class DestructiveBatchRunner
{
    /// <summary>执行一次破坏性动作（首次调用只置待确认；再次调用且键集一致才真正执行）。</summary>
    public static async Task<DestructiveBatchDecision> RunAsync(
        IDestructiveBatchState state,
        string command,
        string emptyMessage,
        Func<int, string> confirmMessage,
        Func<Task<string>> action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(confirmMessage);
        ArgumentNullException.ThrowIfNull(action);

        var snapshot = state.Selection.ToArray();
        if (snapshot.Length == 0)
        {
            state.Result = emptyMessage;
            state.PendingCommand = null;
            return DestructiveBatchDecision.RejectedEmpty;
        }

        var confirmed = string.Equals(state.PendingCommand, command, StringComparison.OrdinalIgnoreCase)
                        && RowSelection.SetEquals(state.PendingKeys, snapshot);
        if (!confirmed)
        {
            state.PendingCommand = command;
            state.PendingKeys = snapshot;
            state.Result = confirmMessage(snapshot.Length);
            return DestructiveBatchDecision.AwaitConfirm;
        }

        state.PendingCommand = null;
        state.PendingKeys = Array.Empty<string>();
        state.Result = await action().ConfigureAwait(false);
        state.ClearSelection();
        return DestructiveBatchDecision.Executed;
    }
}
