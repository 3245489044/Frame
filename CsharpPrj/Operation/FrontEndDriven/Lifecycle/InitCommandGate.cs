#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FrontEndDriven.Lifecycle;

/// <summary>
/// 初始化命令相位门 + pending 重放（B3 / INV-2 相位前置 · INV-3 相位跃迁单写）。
/// <para>
/// 依赖核心的行为在**执行前**断言相位/能力前置（<c>requires</c>）：未达 ⇒ 登记 pending（不执行、不把诊断写面板）。
/// 相位跃迁后由宿主调用 <see cref="ReplayAsync"/> 幂等重放（重放即出队 ⇒ 单拍幂等，重复跃迁不重复执行）。
/// </para>
/// <para>
/// 前置评估器由宿主注册（<see cref="Evaluator"/>）；未注册 = 无门（恒满足）⇒ 零回归。
/// 本件为框架层，不引用 Agent 侧类型（宿主经委托注入）。
/// </para>
/// </summary>
public static class InitCommandGate
{
    /// <summary>前置谓词评估器（由宿主注册；null = 无门，恒满足 ⇒ 零回归）。</summary>
    public static Func<IReadOnlyList<string>, bool>? Evaluator { get; set; }

    private static readonly object _lock = new();
    private static readonly List<(IReadOnlyList<string> Requires, Func<Task> Run)> _pending = new();

    /// <summary>当前 pending 数（结构判据：冷启动 &gt;0，装配后重放归零）。</summary>
    public static int PendingCount
    {
        get { lock (_lock) return _pending.Count; }
    }

    /// <summary>前置满足判定：无声明 / 无评估器 ⇒ true（缺省兼容）。</summary>
    public static bool IsSatisfied(IReadOnlyList<string>? requires)
    {
        if (requires is null || requires.Count == 0)
            return true;
        return Evaluator?.Invoke(requires) ?? true;
    }

    /// <summary>登记待重放命令（未达前置时调用）。</summary>
    public static void RegisterPending(IReadOnlyList<string> requires, Func<Task> run)
    {
        if (requires is null || run is null)
            return;
        lock (_lock) _pending.Add((requires, run));
    }

    /// <summary>
    /// 幂等重放：仅执行当前前置已满足的 pending（出队）；未满足者留队待下次跃迁。返回本次执行数。
    /// </summary>
    public static async Task<int> ReplayAsync()
    {
        var due = new List<(IReadOnlyList<string> Requires, Func<Task> Run)>();
        lock (_lock)
        {
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (!IsSatisfied(_pending[i].Requires))
                    continue;
                due.Add(_pending[i]);
                _pending.RemoveAt(i);
            }
        }

        var executed = 0;
        foreach (var item in due)
        {
            try
            {
                await item.Run().ConfigureAwait(false);
                executed++;
            }
            catch
            {
                // 重放失败不阻断（可见化由命令执行器日志承担）；不重新入队，避免死循环。
            }
        }

        return executed;
    }

    /// <summary>清空 pending（测试 / 重装配用）。</summary>
    public static void Clear()
    {
        lock (_lock) _pending.Clear();
    }
}
