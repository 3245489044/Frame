namespace DataCenter.Protocol.Standards;

/// <summary>
/// 端口驱动请求（用例步骤 → 宿主）：把「步骤上下文」翻译为**域无关形态**（零测试框架依赖、零执行），
/// 使宿主实现（执行面）不必认识测试内核类型，测试侧也不必认识端口机制。
/// <para>
/// 与内核 <c>Strategy.Observability.Ports.IPortInput</c> 的分工：本件是**契约层请求体**（纯数据，无指纹/无执行），
/// 内核接口是执行面入参面；两者形状同源，翻译由接线半承担（出参直传，不复制解析逻辑）。
/// </para>
/// </summary>
/// <param name="PortKey">端口键（<c>&lt;类型/接口&gt;.&lt;方法&gt;</c>）。</param>
/// <param name="Args">位置实参（声明序即语义）。</param>
/// <param name="With">具名实参（结构化原始 JSON 直传）。</param>
/// <param name="Text">内联源文本（供文本解析类端口消费）。</param>
/// <param name="Src">来源锚（纯诊断用）。</param>
/// <param name="InlineStandardJson">
/// 内联 invocation（S2 <c>when invoke</c>）：步骤自带的**端口标准台账 JSON 片段**（<c>{"ports":[&lt;条目&gt;]}</c>）。
/// 非空 ⇒ 宿主按该片段现场装载并执行，**不经端口键查目录**；装载解析仍归标准装载层（<see cref="PortStandardLoader"/> 注入），
/// 宿主不自行解析（避免第二份解析口径）。null = 既有按键路径（存量零回归）。
/// </param>
public sealed record PortInvocationRequest(
    string PortKey,
    IReadOnlyList<object?> Args,
    IReadOnlyDictionary<string, object?> With,
    string? Text = null,
    string? Src = null,
    string? InlineStandardJson = null);

/// <summary>
/// 端口宿主驱动契约（声明 → 可调用边界 → 调用）：**框架层唯一驱动面**，
/// 实现落执行面（<c>Strategy.Observability</c>，与内核同侧，可直接消费 <see cref="PortStandard"/>）。
/// <para>
/// 单一实现点：同一装配现场恰一个宿主（重复装配即失败，对齐 SingleImplementation）。
/// 本契约零测试依赖、零领域词汇——用例侧经**薄适配器**实现测试框架的驱动接口并转发本接口。
/// </para>
/// </summary>
public interface IPortHost
{
    /// <summary>已登记端口键（Ordinal 升序；诊断与守卫取值口）。</summary>
    IReadOnlyList<string> PortKeys { get; }

    /// <summary>是否接管该端口键（未登记 → false，交回既有路径）。</summary>
    bool Handles(string portKey);

    /// <summary>
    /// 调用端口：参数与具名实参由请求体直传（宿主不手写编码解析），返回该步骤的 subject
    /// （调用记录 / 观测事件由宿主侧投影，见 <see cref="Project"/>）。
    /// </summary>
    object? Invoke(PortInvocationRequest request);

    /// <summary>声明端口产物字段（<c>|</c> 分隔；未声明 → false = 弱校验）。</summary>
    bool TryGetProductFields(string portKey, out string productFields);

    /// <summary>求值投影根表达式（<c>Port(…) / Event(…) / Timeline(…) / Aggregate(…)</c>）为可反射取值对象。</summary>
    object? Project(string rootExpression);

    /// <summary>行级隔离：清空调用记录 / 事件游标（每个用例行执行前调用；无状态实现可空操作）。</summary>
    void Reset();
}

/// <summary>
/// 标准装载注入点（**解析与校验唯一归 <c>DataCenter.Standards</c>**）：
/// 本契约只承载「已装载的标准」形状，使宿主不自行解析台账 JSON（避免第二份解析口径）。
/// </summary>
/// <param name="json">标准台账原始 JSON（端口面）。</param>
/// <returns>端口键（Ordinal）→ 已装载标准。</returns>
public delegate IReadOnlyDictionary<string, PortStandard> PortStandardLoader(string json);
