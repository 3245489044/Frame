using Operation.ReconcileDriven.Contracts;
using Operation.ReconcileDriven.Reconcile;
using TConfig;

namespace Operation.ReconcileDriven.Gates;

/// <summary>
/// 装载期对账判据门（C1/C2/C6）：配置装载/校验阶段即拒载不合格声明，避免运行到一半静默。
/// <para>
/// 判据 C3（函数名注册）/C4（参数可解析）/C5（output 目标存在）依赖注册表与渲染树，归既有 Validator 面；
/// 本门只负责与「对账契约自身」相关的静态可判项。
/// </para>
/// </summary>
public sealed class ReconcileContractGate
{
    private readonly ReconcileVocabulary _vocabulary;

    public ReconcileContractGate(ReconcileVocabulary vocabulary)
        => _vocabulary = vocabulary ?? throw new ArgumentNullException(nameof(vocabulary));

    /// <summary>
    /// 校验一份对账声明：通过返回 true；否则返回 false 并给出带判据号的错误消息。
    /// </summary>
    public bool TryValidate(ReconcileConfig? config, out string? error)
    {
        error = null;

        var contract = ReconcileContract.From(config);
        if (contract is null || contract.IsOff)
            return true;   // 无声明 / 显式退出：放行（可审计）

        // C2：mode≠off 时 signals 非空（空契约恒真 ⇒ 拒绝静默通过）
        if (contract.Signals.Count == 0)
        {
            error = $"[{ReconcileCodes.C2EmptyContract}] mode={contract.Mode} 但 signals 为空（空契约恒真，拒载）";
            return false;
        }

        // C1：signal 须在语义词表内（词表为空则跳过，避免无契约场景误拒载）
        if (!_vocabulary.IsEmpty)
        {
            foreach (var s in contract.Signals)
            {
                if (_vocabulary.Contains(s.Signal)) continue;
                var near = _vocabulary.NearCandidates(s.Signal);
                error = $"[{ReconcileCodes.C1UnknownSignal}] signal '{s.Signal}' 不在语义词表内"
                        + (near.Count > 0 ? $"；邻近候选: {string.Join(", ", near)}" : string.Empty);
                return false;
            }
        }

        // C6：order=strict 时序列须可成链（此处以「无重复 signal」为充分检查：重复声明无法构成单调链）
        if (contract.IsStrictOrder)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in contract.Signals)
            {
                if (seen.Add(s.Signal!)) continue;
                error = $"[{ReconcileCodes.C6OrderNotChainable}] signal '{s.Signal}' 在 order=strict 下重复声明，无法构成链";
                return false;
            }
        }

        return true;
    }
}
