namespace DataCenter.Hosting;

/// <summary>
/// 换代载荷（<see cref="GenerationPlan.Export"/> / <see cref="GenerationPlan.Import"/> 的交换值）——
/// 承载<b>不在状态真源里</b>的跨代数迁移数据（S4-2）。
/// <para>
/// 唯一用途（F-11 收窄）：作为 <see cref="GenerationPlan"/> 载荷钩子的交换值 ——
/// <c>DataCenterHost.SwapAsync</c> 在旧代数释放前取 <c>Export</c> 产物、在新代数绑根后交 <c>Import</c> 消费，
/// 用于初始化新代数中"不在真源里"的对象（对象槽按代数重建的输入数据）。除此之外无其他语义。
/// </para>
/// <para>
/// 纪律：状态 KV 与版本序列由真源跨代数复用自动保持连续，<b>禁止</b>把状态快照塞进本载荷
/// （那会造成第二真源与双写；权威: 设计文档 §4 A5"重建 + 状态迁移"、DF1）。
/// <c>Empty</c> 与 <c>new GenerationPayload()</c> 等价（仅为可读性提供的空载荷单例）。
/// </para>
/// </summary>
public sealed class GenerationPayload
{
    /// <summary>空载荷（未导出/无迁移数据；Import 仍会被调用，以便统一其初始化路径）。</summary>
    public static GenerationPayload Empty { get; } = new();

    /// <summary>构造载荷（<paramref name="items"/> 为 null = 空载荷）。</summary>
    public GenerationPayload(IReadOnlyDictionary<string, object?>? items = null)
        => Items = items ?? new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>迁移数据项（键由域定义；不得与状态真源键混用）。</summary>
    public IReadOnlyDictionary<string, object?> Items { get; }

    /// <summary>按键取项（缺失返回 null；不做隐式类型转换）。</summary>
    public object? this[string key] => Items.TryGetValue(key, out var value) ? value : null;
}
