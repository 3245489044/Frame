#nullable enable

namespace UiBackend.Abstractions;

/// <summary>
/// 一拍渲染帧（框架件）：本拍视图键 + 已装配的领域模型。
/// <para>纪律：帧只装配一次——视图键解析、模型装配、根片段与区域节点共用同一实例
/// （禁同拍二次装配：两结果若有隐含时序依赖会不一致）。</para>
/// <para>框架不解释 <typeparamref name="TModel"/>：仅原样透传给领域渲染委托。</para>
/// </summary>
public sealed record UiViewFrame<TModel>(string? ViewKey, TModel Model) where TModel : class;
