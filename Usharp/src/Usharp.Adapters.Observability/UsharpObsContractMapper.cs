using Strategy.Observability.Boundary;
using Share.HierarchySupport;
using Usharp.Syntax;

namespace Usharp.Adapters.Observability;

/// <summary>
/// 观测声明 → 内核契约映射（<c>[Obs]</c> → <c>BoundaryContract</c> / <c>EventContract</c>）。
/// <para>
/// 映射表唯一实现（禁在别处再写一份）：<c>kind=event</c> → <see cref="BoundaryContract.Event"/>；
/// <c>kind=scope</c> / <c>kind=entity</c> → <see cref="BoundaryContract.Data"/>（关联键作用域 / 实体类型词汇表）；
/// <c>[Flow] step emit=</c> → 端口契约的 <c>Emits</c>（既有字段，不新增内核类型）。
/// </para>
/// <para>字段并集复用内核单源 <see cref="FieldSpec.UnionRequiredAndEnums"/>（禁自造字段合并）。</para>
/// </summary>
public static class UsharpObsContractMapper
{
    /// <summary>作用域声明的边界契约键前缀（与实体/事件键同命名空间，避免同键冲突）。</summary>
    public const string ScopeKeyPrefix = "scope:";

    /// <summary>实体声明的边界契约键前缀。</summary>
    public const string EntityKeyPrefix = "entity:";

    /// <summary><c>[Obs] kind=event</c> → 事件边界契约（必填字段 ∪ 枚举域 / 度量键 / 实体类型）。</summary>
    public static BoundaryContract Event(UsharpObsDecl decl)
    {
        ArgumentNullException.ThrowIfNull(decl);
        return BoundaryContract.Event(
            decl.Name,
            FieldSpec.UnionRequiredAndEnums(decl.Required, decl.Enums),
            decl.Metrics,
            decl.Entity);
    }

    /// <summary><c>[Obs] kind=scope</c> → 数据边界契约（会话级作用域键词汇表；<c>parent=</c> 只作闸A 判据，不进契约）。</summary>
    public static BoundaryContract Scope(UsharpObsDecl decl)
    {
        ArgumentNullException.ThrowIfNull(decl);
        return BoundaryContract.Data(
            ScopeKeyPrefix + decl.Name,
            BoundaryDirection.Both,
            scope: DataScope.Session);
    }

    /// <summary><c>[Obs] kind=entity</c> → 数据边界契约（轮级实体类型词汇表；<c>scope=</c> 只作闸A 判据，不进契约）。</summary>
    public static BoundaryContract Entity(UsharpObsDecl decl)
    {
        ArgumentNullException.ThrowIfNull(decl);
        return BoundaryContract.Data(
            EntityKeyPrefix + decl.Name,
            BoundaryDirection.Both,
            scope: DataScope.Round);
    }

    /// <summary><c>[Flow] F step &lt;key&gt; emit=k1,k2</c> → 端口契约（<c>Emits</c> 即发射绑定）。</summary>
    public static BoundaryContract FlowStep(string flowId, string stepKey, IEnumerable<string> eventKinds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepKey);
        ArgumentNullException.ThrowIfNull(eventKinds);

        return BoundaryContract.Port($"{flowId}.{stepKey}", emits: eventKinds.ToArray());
    }
}
