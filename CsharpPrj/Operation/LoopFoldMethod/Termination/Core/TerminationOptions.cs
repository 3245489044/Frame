namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 循环终止选项（L3 可配，单一声明点）。宿主只做「环境变量 → 本对象」绑定，
/// 禁止在消费方各写一套阈值（对齐 clinerules §9 参数收敛）。
/// </summary>
/// <param name="StagnationLimit">空转上限（macro 判据连续无推进轮数）。</param>
/// <param name="SameFingerprintRounds">同指纹连续轮数上限（死循环识别）。0 = 关闭。</param>
/// <param name="TerminateOnStagnation">空转达上限是否终止（false = 仅可见化 report，非终止）。</param>
/// <param name="HaltOnFailure">失败是否立即终止（HaltOnFailure；命令/脚本宿主默认 true）。</param>
/// <param name="MaxRounds">轮次硬上限（任何循环必须有界的最后一道兜底）。</param>
public sealed record TerminationOptions(
    int StagnationLimit = 2,
    int SameFingerprintRounds = 3,
    bool TerminateOnStagnation = false,
    bool HaltOnFailure = true,
    int MaxRounds = 1000)
{
    public static TerminationOptions Default { get; } = new();
}

