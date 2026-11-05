using Modules.KeySpace.Model;

namespace Modules.KeySpace.Dispatch;

/// <summary>目标可执行性状态（Gate 段唯一判定域）。</summary>
public enum TargetState
{
    /// <summary>可执行（目标存在且未休眠）。</summary>
    Executable,

    /// <summary>目标不存在（剔除并计数）。</summary>
    Missing,

    /// <summary>休眠中（转 Activate 段，不直接分发）。</summary>
    Dormant,

    /// <summary>禁止分发（策略拒绝）。</summary>
    Forbidden,
}

/// <summary>Gate 判定：目标 + 状态 + 原因码（原因码为受控短词，禁自由文本）。</summary>
public readonly record struct GateVerdict(KeyRef Target, TargetState State, string ReasonCode)
{
    /// <summary>是否放行到 Activate/Dispatch。</summary>
    public bool IsExecutable => State == TargetState.Executable;
}

/// <summary>可执行性门端口（Gate 段唯一入口；实现可注入，缺省 <see cref="ExecutableTargetGate"/>）。</summary>
public interface ITargetGate
{
    /// <summary>判定单个目标（纯函数或窄端口；禁顺手做归一/规划）。</summary>
    GateVerdict Check(KeyRef target);
}

/// <summary>
/// 缺省门：全部放行（零回归）。
/// <para>目标存在性/休眠态由宿主装配具体门（例如任务查询端口）覆盖，避免框架依赖任务池。</para>
/// </summary>
public sealed class ExecutableTargetGate : ITargetGate
{
    /// <summary>单例（无状态）。</summary>
    public static readonly ExecutableTargetGate Instance = new();

    public GateVerdict Check(KeyRef target) => new(target, TargetState.Executable, "executable");
}
