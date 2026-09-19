using Strategy.Observability.Ports;

namespace Strategy.Observability.SpecHost;

/// <summary>
/// 端口面（台账 / 标准目录的可注入投影）：宿主只消费「键集 + 接管判定 + 产物字段 + 内联实现面」，
/// **不认识台账文件格式**（读 JSON 与过渡期元数据是装配方的事）。
/// </summary>
public interface ISpecPortSurface
{
    /// <summary>端口键（Ordinal 升序；= 路由面，宿主仅接管其中键）。</summary>
    IReadOnlyList<string> PortKeys { get; }

    /// <summary>
    /// 是否接管该端口键（未登记 → false，交回旧 kind 路径）。
    /// </summary>
    bool Handles(string portKey);

    /// <summary>
    /// 步骤名 → 端口键归一（路线 B：<c>.specs</c> 步骤名 = 冻结 ID（历史 kind 名，永不改），台账键 = 实现承载位）。
    /// 缺省实现 = 已接管即自身（不认识步骤别名的端口面零回归）。
    /// <para>实现方禁用 <see cref="Handles"/> 作实现体——缺省实现经 <see cref="Handles"/> 归一，互指即无限递归。</para>
    /// </summary>
    bool TryResolveStep(string step, out string portKey)
    {
        portKey = step;
        return Handles(step);
    }

    /// <summary>声明端口产物字段（<c>|</c> 分隔；未声明 → false = 弱校验）。</summary>
    bool TryGetProductFields(string portKey, out string productFields);

    /// <summary>已登记内联实现（台账声明 <c>binding: inline</c>）端口键（Ordinal 升序）：守卫对账用。</summary>
    IReadOnlyList<string> InlinePortKeys { get; }

    /// <summary>
    /// 已播种端口契约键（Ordinal 升序）：含「只登记未绑定」的声明面（生产台账播种者亦在内），
    /// 供守卫验证契约面并集（区别于 <see cref="PortKeys"/> 的可路由面）。
    /// </summary>
    IReadOnlyList<string> ContractKeys { get; }

    /// <summary>
    /// 装配期绑定工厂（P4 替身隔离：逐行撤销上一行注入的替身时，用它回填端口的装配期实现）。
    /// 返回 <c>null</c> = 该端口无装配期绑定（冻结 / 未登记），宿主据此跳过回填。默认实现返回 null
    /// （不认识装配期绑定的端口面零回归）。
    /// </summary>
    Func<IPortBinding>? BindingFactory(string portKey) => null;
}
