using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// facet 视图集（**D-5 单一实现**）：<see cref="UsharpCompilation"/> 与 <see cref="UsharpGateModel"/>
/// 共同继承，核心/领域视图只在本类声明一次。
/// <para>
/// 必填/可选语义由子类通过 <see cref="View{T}"/> 决定：
/// 缺省「未注册 ⇒ 空表」（闸A 模型语义）；<see cref="UsharpCompilation"/> 覆写为「未注册 ⇒ 抛」
/// （编译门面语义，禁把装配错误静默成空声明面）。
/// </para>
/// </summary>
public abstract class UsharpFacetViewSet : IUsharpFacetReader
{
    /// <summary>绑定 facet 注册表。</summary>
    /// <param name="facets">本次编译的 facet 注册表。</param>
    protected UsharpFacetViewSet(UsharpFacetRegistry facets)
    {
        ArgumentNullException.ThrowIfNull(facets);
        Facets = facets;
    }

    /// <summary>facet 注册表（子类构造/装配用；读取一律经 <see cref="Facet{T}"/>）。</summary>
    private protected UsharpFacetRegistry Facets { get; }

    /// <inheritdoc/>
    public T Facet<T>() where T : class => Facets.Facet<T>();

    /// <inheritdoc/>
    public bool TryGetFacet<T>(out T facet) where T : class => Facets.TryGetFacet(out facet);

    /// <summary>列表视图判据（缺省：未注册 ⇒ 空表；子类可覆写为必填）。</summary>
    /// <typeparam name="T">列表元素类型（facet 类型 = <c>IReadOnlyList&lt;T&gt;</c>）。</typeparam>
    protected virtual IReadOnlyList<T> View<T>() where T : class
        => TryGetFacet<IReadOnlyList<T>>(out var list) ? list : Array.Empty<T>();

    // ── 核心语言面（必填：缺 provider 属装配错误）──────────────────────────

    public SpaceRegistry Spaces => Facet<SpaceRegistry>();

    public IReadOnlyList<UsharpMatrixDecl> Matrices => Facet<IReadOnlyList<UsharpMatrixDecl>>();

    /// <summary><c>[Matrix]</c> 判据读法投影（E5：闸A 与装配共读，禁各自解析）。</summary>
    public IReadOnlyDictionary<string, UsharpDecisionDecl> Decisions
        => Facet<IReadOnlyDictionary<string, UsharpDecisionDecl>>();

    public IReadOnlyList<UsharpFlowDecl> Flows => Facet<IReadOnlyList<UsharpFlowDecl>>();

    public IReadOnlyList<UsharpRefDecl> Refs => Facet<IReadOnlyList<UsharpRefDecl>>();

    // ── 领域面（`View<T>()`：缺省空表，编译门面覆写为必填）────────────────

    /// <summary><c>[Domain]</c> 域定义声明（U1；空表 = 本编译无域定义面）。</summary>
    public IReadOnlyList<UsharpDomainDecl> Domains => View<UsharpDomainDecl>();

    /// <summary><c>[Gather]</c> 聚拢声明（U2；空表 = 本编译无收集端口声明）。</summary>
    public IReadOnlyList<UsharpGatherDecl> Gathers => View<UsharpGatherDecl>();

    /// <summary><c>[Entity]</c> 语义轴声明（空表 = 无实体语义面）。</summary>
    public IReadOnlyList<UsharpEntityDecl> Entities => View<UsharpEntityDecl>();

    /// <summary><c>[KeyLexicon]</c> 受控词表声明（空表 = 无词表面）。</summary>
    public IReadOnlyList<UsharpKeyLexiconDecl> KeyLexicons => View<UsharpKeyLexiconDecl>();

    /// <summary><c>[AnswerShape]</c> / <c>[WindowShape]</c> 形状契约（空表 = 无形状面）。</summary>
    public IReadOnlyList<UsharpKeyShapeDecl> KeyShapes => View<UsharpKeyShapeDecl>();

    /// <summary><c>[KeySpace]</c> 键空间声明（空表 = 本编译无键声明面）。</summary>
    public IReadOnlyList<UsharpKeySpaceDecl> KeySpaces => View<UsharpKeySpaceDecl>();

    /// <summary><c>[Command]</c> 连接点命令声明（空表 = 本编译无命令声明面）。</summary>
    public IReadOnlyList<UsharpCommandDecl> Commands => View<UsharpCommandDecl>();

    /// <summary><c>[ReadModel]</c> 读模型声明（空表 = 本编译无读模型声明面）。</summary>
    public IReadOnlyList<UsharpReadModelDecl> ReadModels => View<UsharpReadModelDecl>();

    /// <summary><c>[Product]</c> 产品声明（空表 = 本编译无产品声明面）。</summary>
    public IReadOnlyList<UsharpProductDecl> Products => View<UsharpProductDecl>();

    /// <summary><c>[Face]</c> 面声明（空表 = 本编译无面声明面；有面即肯定式白名单生效）。</summary>
    public IReadOnlyList<UsharpFaceDecl> Faces => View<UsharpFaceDecl>();

    /// <summary><c>[Relation]</c> 关系操作声明（空表 = 无关系操作声明面）。</summary>
    public IReadOnlyList<UsharpRelationDecl> Relations => View<UsharpRelationDecl>();

    /// <summary><c>[Invariant]</c> 关系不变量声明（空表 = 无关系不变量）。</summary>
    public IReadOnlyList<UsharpInvariantDecl> Invariants => View<UsharpInvariantDecl>();

    /// <summary><c>[Derive]</c> 派生成员集声明（空表 = 无派生成员集）。</summary>
    public IReadOnlyList<UsharpDeriveDecl> Derivations => View<UsharpDeriveDecl>();

    /// <summary><c>[KeyPolicy]</c> 写侧策略声明（空表 = 无写侧策略面）。</summary>
    public IReadOnlyList<UsharpIdentityPolicyDecl> IdentityPolicies => View<UsharpIdentityPolicyDecl>();

    /// <summary><c>[KeyBridge]</c> 跨域映射声明（空表 = 无跨域映射）。</summary>
    public IReadOnlyList<UsharpKeyBridgeDecl> KeyBridges => View<UsharpKeyBridgeDecl>();

    /// <summary><c>[Mount]</c> 挂载绑定声明（空表 = 本编译无绑定面）。</summary>
    public IReadOnlyList<UsharpMountDecl> Mounts => View<UsharpMountDecl>();

    /// <summary><c>[Table]</c> 表结构声明（空表 = 本编译无数据层）。</summary>
    public IReadOnlyList<UsharpTableDecl> Tables => View<UsharpTableDecl>();

    /// <summary><c>[Record]</c> 记录载荷声明（空表 = 本编译无记录面）。</summary>
    public IReadOnlyList<UsharpRecordDecl> Records => View<UsharpRecordDecl>();

    /// <summary><c>[Rule]</c> 规则声明（空表 = 本编译无规则面）。</summary>
    public IReadOnlyList<UsharpRuleDecl> Rules => View<UsharpRuleDecl>();

    /// <summary><c>[Config]</c> 配置声明（空表 = 本编译无配置面）。</summary>
    public IReadOnlyList<UsharpConfigDecl> Configs => View<UsharpConfigDecl>();

    /// <summary><c>[Const]</c> 成员级声明（空表 = 本编译无成员声明面）。</summary>
    public IReadOnlyList<UsharpConstDecl> Consts => View<UsharpConstDecl>();
}
