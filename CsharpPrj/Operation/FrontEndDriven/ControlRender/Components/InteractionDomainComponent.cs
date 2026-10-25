using ECS.Core;

namespace FrontEndDriven.ECS.Components;

/// <summary>
/// 交互域标记（A1）：区分「有物理控件的 host 实体」与「仅声明存在的 page 实体」。
/// 消费方：EventBindingSystem（page 无物理控件，禁物理挂接）；诊断/门禁按域统计。
/// </summary>
public struct InteractionDomainComponent : IComponent
{
    /// <summary>域取值：<see cref="TConfig.InteractionDomains.HostName"/> / <see cref="TConfig.InteractionDomains.PageName"/>。</summary>
    public string Domain;
}
