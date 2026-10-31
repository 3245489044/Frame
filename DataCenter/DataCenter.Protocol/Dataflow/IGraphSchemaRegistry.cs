namespace DataCenter.Protocol.Dataflow;

/// <summary>
/// 图模式注册表的<b>只读</b>消费面（形态同 <c>IStateRegistry</c>：装配期注册、运行期只读）。
/// 注册动作在 Core 的具体实现上（<c>WithProduct/WithLevel/...</c>），本接口只出读面。
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §4.1。
/// </summary>
public interface IGraphSchemaRegistry
{
    /// <summary>当前模式声明（不可变快照）。</summary>
    GraphSchemaSpec Schema { get; }

    /// <summary>按产品键取声明（未登记 ⇒ false，不伪造默认产品）。</summary>
    bool TryGetProduct(string productId, out DataProductSpec product);
}
