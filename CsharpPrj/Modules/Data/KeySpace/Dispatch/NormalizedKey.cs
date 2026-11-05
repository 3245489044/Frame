using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Model;

namespace Modules.KeySpace.Dispatch;

/// <summary>归一化结果分类（分发目标闭合 DTC：Resolve → Normalize 段的唯一出口语义）。</summary>
public enum NormalizeOutcome
{
    /// <summary>命中族身份模板（原文即业务键形态）。</summary>
    Resolved,

    /// <summary>命中族别名模板（称呼还原）。</summary>
    AliasRestored,

    /// <summary>经业务键投影还原（实例键/派生前缀 → 业务键）。</summary>
    BusinessKeyRestored,

    /// <summary>不可还原（不静默保留；由调用方计入可见计数）。</summary>
    Unresolvable,

    /// <summary>族未知（声明面未覆盖）。</summary>
    UnknownFamily,
}

/// <summary>
/// 归一化结果：目标键 + 原文 + 结果分类 + 原因码。
/// <para>纪律：不可还原 ⇒ <see cref="Key"/> = null 且 <see cref="Outcome"/> 可见，禁以原文冒充业务键。</para>
/// </summary>
public readonly record struct NormalizedKey(
    KeyRef? Key,
    string Raw,
    NormalizeOutcome Outcome,
    string? ReasonCode)
{
    /// <summary>是否得到可进入规划段的业务键。</summary>
    public bool IsResolved => Key is not null;

    /// <summary>业务键值（未还原 ⇒ 空串，不伪造）。</summary>
    public string Value => Key?.Value ?? string.Empty;
}

/// <summary>
/// 业务键投影端口（可选）：当实例键/派生前缀无法由文法反解时，由领域适配器给出业务键。
/// <para>框架只声明端口，领域词与映射表留在产品侧（Frame 层零领域词）。</para>
/// </summary>
public interface IBusinessKeyResolver
{
    /// <summary>把原始成员还原为业务键引用；不可还原 ⇒ null（不猜）。</summary>
    KeyRef? Resolve(string raw, IKeySpaceDeclaration declaration);
}

/// <summary>目标归一化唯一入口（分发管线 Normalize 段）。</summary>
public interface ITargetNormalizer
{
    /// <summary>声明面是否提供了分发目标族（false ⇒ 归一化不可用，必须显式可见而非静默空集）。</summary>
    bool IsConfigured { get; }

    /// <summary>归一化单个原始成员（纯函数，无 IO）。</summary>
    NormalizedKey Normalize(string raw);
}
