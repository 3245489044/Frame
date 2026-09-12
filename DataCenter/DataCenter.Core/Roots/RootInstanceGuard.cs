using DataCenter.Protocol.Roots;

namespace Operation.StateBlock.Roots;

/// <summary>
/// 单根守卫（DF8）—— 进程内根实例 ≤ 1：第二个构造即 fail-fast（不给"平行根/兜底造根"机会）。
/// 判据（设计文档 §12.7 R1）：第二次构造 → fail-fast；缺根 → <see cref="RootMissingException"/>。
/// </summary>
internal static class RootInstanceGuard
{
    private static int _live;

    /// <summary>进入（已存在存活根 → 抛错，且不改变计数）。</summary>
    internal static void Enter()
    {
        if (Interlocked.Increment(ref _live) != 1)
        {
            Interlocked.Decrement(ref _live);
            throw new InvalidOperationException(
                "进程内已存在 DataCenter 根实例（DF8：整体数据流由单一根实例持有；禁止平行根/兜底造根）。");
        }
    }

    /// <summary>退出（释放根）；与 <see cref="Enter"/> 严格配对且仅一次。</summary>
    internal static void Exit() => Interlocked.Decrement(ref _live);
}
