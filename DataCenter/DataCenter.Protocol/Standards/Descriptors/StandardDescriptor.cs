namespace DataCenter.Protocol.Standards.Descriptors;

/// <summary>
/// 成员形态（描述符单源化 §2.1）——一个模型成员在 JSON 里的承载形态；
/// 决定读取器分派、遍历下钻、结构门属性类型与镜像位序。
/// <para>
/// 纪律：形态是**结构事实**，不含领域语义；本质语义（自递归来源 / 长短形态判别 / 跨字段条件）
/// 归 <see cref="MemberKind.Opaque"/>，由命名钩子接管（见设计文档 §7 分级覆盖）。
/// </para>
/// </summary>
public enum MemberKind
{
    /// <summary>标量 / 字符串（原样）。</summary>
    Scalar,

    /// <summary>词表枚举（取值须 ∈ <see cref="MemberDescriptor.Vocabulary"/>）。</summary>
    Enum,

    /// <summary>单子对象（下钻到 <see cref="MemberDescriptor.ChildLayer"/>）。</summary>
    Child,

    /// <summary>子对象序列（逐项下钻到 <see cref="MemberDescriptor.ChildLayer"/>）。</summary>
    Children,

    /// <summary>键值映射（定键或开放映射 <see cref="LayerDescriptor.OpenMap"/>）。</summary>
    KeyedMap,

    /// <summary>原样 JSON 载荷（<c>value</c> / <c>literal</c> / <c>default</c> / <c>rows</c> …，不解释内部）。</summary>
    Payload,

    /// <summary>保留手写：本质语义（自递归来源 / 长短形态判别 / 跨字段条件），由命名钩子接管。</summary>
    Opaque,
}

/// <summary>
/// 长 / 短双形态声明（<c>first</c> / <c>last</c> / <c>agg</c> / <c>filter</c> / <c>dict</c>）：
/// 同一算子的「内嵌对象（长形）」与「平铺键（短形）」两种书写。缺省 = 只认短形（平铺键）。
/// </summary>
/// <param name="LongKey">长形平铺键（内嵌对象展开时读取的键；如 <c>first</c>）。</param>
/// <param name="ShortKey">短形平铺键（直接平铺；如 <c>firstOrNull</c>）。</param>
public sealed record Polymorphy(string LongKey, string? ShortKey = null);

/// <summary>
/// 成员描述（描述符单源化 §2.1）：模型成员 ↔ JSON 键 ↔ 形态 ↔ 词表 ↔ 子层。
/// <para>它是 T1（键白名单）/ T2（<c>ExprSlot</c>）/ T3（结构门绑定）/ T4（家族落点）/ T5（镜像位序）
/// 五张表的**共同上游**：五处均为本记录某字段的纯派生。</para>
/// </summary>
/// <param name="Member">CLR 属性名（与承载类型严格同名，反射可对拍）。</param>
/// <param name="JsonKey">主平铺 JSON 键；null = 该成员无平铺键（仅在其宿主对象内出现）。</param>
/// <param name="Kind">形态（见 <see cref="MemberKind"/>）。</param>
/// <param name="Vocabulary">词表引用（<c>PortImplVocabulary</c> 的成员名）；仅 <see cref="MemberKind.Enum"/> 用。</param>
/// <param name="Required">结构门 <c>required</c> / 读取期必填。</param>
/// <param name="ChildLayer">子层名（<see cref="MemberKind.Child"/> / <see cref="MemberKind.Children"/> 用）。</param>
/// <param name="NestedKeys">对象形态内读取的子键（如 <c>fold{over,init,step}</c>）；只读登记，不闭合。</param>
/// <param name="OperatorGroup">算子组（同组成员共属一个算子；C10「算子组恰一」的取值域取自本字段）。</param>
/// <param name="Polymorphy">长 / 短双形态（可空 = 只认短形）。</param>
/// <param name="AliasKeys">别名键（同一成员的**简写形态**；如 <c>from:arg</c> 简写下的 <c>arg</c> → <see cref="ValueSourceStandard.Index"/>）；半规则层用。</param>
/// <param name="Owner">成员归属模型（扁平多模型层用；null = 归属 <see cref="LayerDescriptor.Model"/>）。</param>
/// <param name="EmptyOnAbsent">
/// 键缺失时归一无（<see cref="MemberKind.Children"/> → 空序列；<see cref="MemberKind.KeyedMap"/> → 空映射）而非 null，
/// 用于复现手写读取器的 <c>[] vs null</c> 口径（如 <c>impl.args</c> / <c>impl.deps</c> 归一为空序列）。
/// </param>
public sealed record MemberDescriptor(
    string Member,
    string? JsonKey,
    MemberKind Kind,
    string? Vocabulary = null,
    bool Required = false,
    string? ChildLayer = null,
    IReadOnlyList<string>? NestedKeys = null,
    string? OperatorGroup = null,
    Polymorphy? Polymorphy = null,
    IReadOnlyList<string>? AliasKeys = null,
    Type? Owner = null,
    bool EmptyOnAbsent = false,
    string? Reader = null,
    string? Gate = null)
{
    /// <summary>归属模型（<see cref="Owner"/> 为 null 时回落）；供反射对拍与遍历取值。</summary>
    public Type ModelOf(Type layerModel) => Owner ?? layerModel;

    /// <summary>本成员承载的全部 JSON 键（主键 + 别名键 + 宿主对象嵌套子键；序 = 登记序）。</summary>
    public IEnumerable<string> DeclaredKeys
    {
        get
        {
            if (JsonKey is not null) yield return JsonKey;
            if (AliasKeys is not null)
                foreach (var alias in AliasKeys) yield return alias;
            if (NestedKeys is not null)
                foreach (var key in NestedKeys) yield return key;
        }
    }

    /// <summary>是否为子节点槽（<see cref="MemberKind.Child"/> / <see cref="MemberKind.Children"/> / 承载子对象的 <see cref="MemberKind.KeyedMap"/>）。</summary>
    public bool IsChild => Kind is MemberKind.Child or MemberKind.Children or MemberKind.KeyedMap;
}

/// <summary>
/// 层描述（描述符单源化 §2.1）：一个 JSON 对象层（<c>impl</c> / <c>dep</c> / <c>step</c> / <c>scope</c> /
/// <c>deliver</c> / <c>valueSource</c> / <c>expr</c> …）。
/// <para>成员声明序 = 语义位序（结构门键序 / 镜像位序据此派生）；本表是唯一手写真源。</para>
/// </summary>
/// <param name="Layer">规范层名（= schema <c>$defs</c> 名，如 <c>impl</c> / <c>arg</c> / <c>dep</c> / <c>expr</c>）。</param>
/// <param name="KeysLevel">键表 level（= <c>PortImplVocabulary.KeysOf</c> 的 level 名，如 <c>ImplKeys</c> / <c>ArgKeys</c>）。</param>
/// <param name="Model">承载类型（如 <c>PortImplStandard</c> / <c>ExprStandard</c>）。</param>
/// <param name="Members">成员表（声明序即语义）。</param>
/// <param name="OpenMap">开放映射（<c>impl.bind</c> / <c>scenarios.cases</c>）：键为不透明名，不做闭合。</param>
/// <param name="Ordered">键序即语义（结构门 <c>properties</c> 序）——false = 多形态/扁平层，键序无语义（对拍按键集合）。</param>
public sealed record LayerDescriptor(
    string Layer,
    string KeysLevel,
    Type Model,
    IReadOnlyList<MemberDescriptor> Members,
    bool OpenMap = false,
    bool Ordered = true,
    string? Reader = null)
{
    /// <summary>本层合法键集（平铺键 ∪ 嵌套子键；保序去重）= T1 键白名单与结构门 <c>properties</c> 的取值域。</summary>
    public IReadOnlyList<string> Keys
    {
        get
        {
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in Members)
                foreach (var key in member.DeclaredKeys)
                    if (seen.Add(key)) keys.Add(key);

            return keys;
        }
    }
}
