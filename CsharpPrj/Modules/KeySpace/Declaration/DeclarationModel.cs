using System;
using System.Collections.Generic;
using System.Linq;
using Modules.KeySpace.Model;

namespace Modules.Declaration;

/// <summary>声明种类（与 .ushar 声明面一一对应）。</summary>
public enum DeclarationKind
{
    Space,
    Node,
    Flow,
    Matrix,
    Reference,
}

/// <summary>闭合校验姿态（装配期）：Report = 只记清单（迁移期）；Throw = 任一违规即抛（生产）。</summary>
public enum ClosureStrictness
{
    Report,
    Throw,
}

/// <summary>端口绑定（Node 专用）：方向 + 必填 + 所属族。</summary>
public sealed record PortBinding(string Name, bool Inbound, bool Required, string FamilyId);

/// <summary>
/// 一条声明 ↔ 一份实现的绑定（BindingManifest 条目）。
/// <para>判据载体 = 标识与端口集合（结构事实），不含动机/收益等执行者叙述。</para>
/// </summary>
public sealed record DeclarationBinding(
    KeyDomainId Domain,
    string DeclarationId,
    DeclarationKind Kind,
    string ImplId,
    IReadOnlyList<PortBinding> Ports,
    string SourceFingerprint);

/// <summary>绑定清单（冻结产物）：运行期唯一只读事实。</summary>
public sealed record BindingManifest(string Fingerprint, IReadOnlyList<DeclarationBinding> Bindings)
{
    public static BindingManifest Empty { get; } = new(string.Empty, Array.Empty<DeclarationBinding>());

    public bool IsFrozen => !string.IsNullOrWhiteSpace(Fingerprint);

    /// <summary>按声明标识定位绑定（多绑定 = 违规，由校验器裁决）。</summary>
    public IReadOnlyList<DeclarationBinding> Find(string declarationId)
        => Bindings.Where(b => string.Equals(b.DeclarationId, declarationId, StringComparison.Ordinal)).ToArray();
}

/// <summary>声明侧节点（[Node] fn + inports + outports）。</summary>
public sealed record DeclaredNode(
    string NodeKey,
    string Function,
    IReadOnlyList<string> Inports,
    IReadOnlyList<string> Outports);

/// <summary>声明侧步骤（[Flow] step）。</summary>
public sealed record DeclaredStep(
    string FlowId,
    string StepKey,
    string Function,
    IReadOnlyList<string> ResultPorts);

/// <summary>声明目录（只读投影；装配期从声明源一次性装载）。</summary>
public sealed record DeclarationCatalog(
    string SourceFingerprint,
    IReadOnlyList<DeclaredNode> Nodes,
    IReadOnlyList<DeclaredStep> Steps)
{
    public static DeclarationCatalog Empty { get; } =
        new(string.Empty, Array.Empty<DeclaredNode>(), Array.Empty<DeclaredStep>());

    public bool IsEmpty => Nodes.Count == 0 && Steps.Count == 0;
}

/// <summary>闭合违规（结构码 + 点名载荷）。</summary>
public sealed record DeclarationViolation(
    string Code,
    string Detail,
    string DeclarationId,
    string? ImplId);
