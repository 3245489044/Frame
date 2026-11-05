namespace Modules.Wiring.Runtime;

/// <summary>
/// 投递模式：<see cref="Shadow"/> 只做判定与记账（不执行实现，用于旧链对拍）；
/// <see cref="Primary"/> 为唯一生产投递（执行实现并记账）。
/// </summary>
public enum WireDeliveryMode
{
    Shadow,
    Primary,
}
