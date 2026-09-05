using Plate.DslMethod.Schema;

namespace Operation.DataStoreDriven.ReferenceSchema;

/// <summary>
/// 引用类型词汇表端口：以编译后的引用 Schema（[Edge] 声明）为权威，提供「某引用类型是否已声明 / 是否已废弃」判定。
/// 供 <see cref="Core.UniversalReferenceResourceReferenceStore"/> 在写入前 fail-fast 校验引用类型，
/// 替代原先把未知类型静默降级为 <c>ReferenceType.Other</c> 的行为。
/// </summary>
public interface IReferenceTypeRegistry
{
    /// <summary>该引用类型是否已在 DSL 词汇表声明（[Edge]）。</summary>
    bool IsDeclared(string edgeName);

    /// <summary>该引用类型是否已标记废弃（dep:replaced-by(Y)），禁止新增引用。</summary>
    bool IsDeprecated(string edgeName);

    /// <summary>按名取边声明；未声明返回 null。</summary>
    ReferenceEdgeType? FindEdge(string edgeName);

    /// <summary>
    /// 解析某节点类型的端口类型签名（<c>[Node] inports/outports</c> 声明）。
    /// 节点类型或端口未声明 → false（WorkflowCompiler 对未声明类型约束的端口跳过类型校验，可空守卫）。
    /// </summary>
    bool TryGetPortType(string nodeType, string port, bool isInput, out string type);
}
