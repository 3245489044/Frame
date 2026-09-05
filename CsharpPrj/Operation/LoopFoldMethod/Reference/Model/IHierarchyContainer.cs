using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Reference.Model;

/// <summary>
/// 层级容器抽象（Reference 空间维度）—— History / Task / Node 的统一**只读投影**容器。
/// 只读：供投影（RefHead）/ 纯推演（Derivation）读取层级与出边；写边（何时建/挂/查）唯一 owner 在
/// DataStoreDriven 写边原语（<c>IReferenceMaintenance</c> / <c>IResourceReferenceStore</c>），
/// 本抽象不复制任何写边逻辑（TH-12 单一实现）。
/// </summary>
/// <typeparam name="T">下一层容器类型（History→Task→Node 逐层收窄）。</typeparam>
public interface IHierarchyContainer<out T>
{
    /// <summary>本容器的稳定 Id（如 history.{id} / taskKey / wf.node）。</summary>
    string Id { get; }

    /// <summary>下一层子容器集合（层级/包含关系）。</summary>
    IReadOnlyList<T> Children { get; }

    /// <summary>本节点的出边集合（该节点在引用图上的出向引用关系）。</summary>
    IReadOnlyList<ReferenceEdge> OutEdges { get; }
}
