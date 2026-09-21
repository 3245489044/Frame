using Plate.DslMethod;
using Plate.DslMethod.Logic;
using Plate.DslMethod.Model;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// Usharp 闸A 领域模型：把「已声明集 + 待校验声明 + 共享索引」装配为一次校验的只读输入。
/// 由 <see cref="UsharpValidator.Validate"/> 构造，喂给内核 <c>GateEngine&lt;UsharpGateModel&gt;</c>；
/// 规则只读本模型，不产生副作用（对齐「本层只校验不执行」）。
/// </summary>
public sealed class UsharpGateModel
{
    public UsharpGateModel(
        IReadOnlyList<DslDocument> documents,
        DslIndex index,
        SpaceRegistry spaces,
        IReadOnlyList<UsharpMatrixDecl> matrices,
        IReadOnlyList<UsharpFlowDecl> flows,
        IReadOnlyList<UsharpRefDecl> refs,
        ObsRegistry? obs)
    {
        Documents = documents;
        Index = index;
        Spaces = spaces;
        Matrices = matrices;
        Flows = flows;
        Refs = refs;
        Obs = obs;

        // 共享索引：一次构造，规则间复用（避免每条规则重复枚举）。
        FlowIds = new HashSet<string>(flows.Select(f => f.FlowId), StringComparer.Ordinal);
        NodeNames = new HashSet<string>(
            documents.SelectMany(d => d.Declarations)
                     .Where(d => d.Kind == DslSymbolKind.Node)
                     .Select(d => d.Name), StringComparer.Ordinal);
    }

    public IReadOnlyList<DslDocument> Documents { get; }

    public DslIndex Index { get; }

    public SpaceRegistry Spaces { get; }

    public IReadOnlyList<UsharpMatrixDecl> Matrices { get; }

    public IReadOnlyList<UsharpFlowDecl> Flows { get; }

    public IReadOnlyList<UsharpRefDecl> Refs { get; }

    /// <summary>观测声明注册中心（可为 null：未启用 <c>[Obs]</c> 闸A 时）。</summary>
    public ObsRegistry? Obs { get; }

    /// <summary>已声明流 id 集（规则 ③ 复用）。</summary>
    public HashSet<string> FlowIds { get; }

    /// <summary>已声明 <c>[Node]</c> 名集（规则 ③ 告警判据复用）。</summary>
    public HashSet<string> NodeNames { get; }
}
