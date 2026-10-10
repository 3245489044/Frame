using System.Collections.Generic;
using Modules.Node.Relations;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 声明引用源端口（G3/G4）：装配期由 USharp 声明（workflows + reference + placement + domains）投影出权威边，
/// 取代运行期从节点内 <c>RelationsJson</c> 推导。权威链 = .ushar 声明 → 引用库 → 框架管理（D3/D4）。
/// <para>缺省未装配 ⇒ 回退 RelationsJson 兼容读（G4 退役方向；零行为变更）。</para>
/// </summary>
public interface IDeclaredReferenceSource
{
    /// <summary>本声明源覆盖的工作流 Id 集（declared <c>[Flow]</c> 标识；供装配期枚举构建）。</summary>
    IReadOnlyList<string> WorkflowIds { get; }

    /// <summary>取某工作流的静态边集（控制流 Calls / 组合 Owns / 位置 at·contains / 导出·继承·依赖等）。</summary>
    IReadOnlyList<StaticReference> GetStaticEdges(string workflowId);

    /// <summary>取某节点的数据流/键映射关系边（声明源，构建期由 inports/outports 确定）。</summary>
    IReadOnlyList<NodeRelation> GetNodeRelations(string workflowId, string nodeKey);
}

/// <summary>空声明源（未装配占位）：空集，语义等价于「无声明 ⇒ 回退兼容读」。</summary>
public sealed class NullDeclaredReferenceSource : IDeclaredReferenceSource
{
    /// <summary>共享单例（无状态）。</summary>
    public static readonly NullDeclaredReferenceSource Instance = new();

    /// <inheritdoc />
    public IReadOnlyList<string> WorkflowIds => System.Array.Empty<string>();

    /// <inheritdoc />
    public IReadOnlyList<StaticReference> GetStaticEdges(string workflowId)
        => System.Array.Empty<StaticReference>();

    /// <inheritdoc />
    public IReadOnlyList<NodeRelation> GetNodeRelations(string workflowId, string nodeKey)
        => System.Array.Empty<NodeRelation>();
}
