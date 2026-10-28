using System.Collections.Generic;

namespace Operation.HierarchyMethod.History.Ports;

/// <summary>
/// 历史键文法端口（框架声明，宿主实现）—— 把域专属的键前缀/模板/身份值袋约定注入中性内容层。
/// <para>
/// 存在理由（SingleImplementation）：历史内容层（切分 / 物化 / 重组 / 追加 / 归档）的机制是域无关的，
/// 唯有「键长什么样、值袋怎么装」随域变化（如 Agent 的 <c>seg.{container}</c> /
/// <c>{container}.r{round}.{kind}.{ordinal}</c>）。框架本体零宿主依赖 ⇒ 键文法经本端口注入。
/// </para>
/// </summary>
public interface IHistoryKeyGrammar
{
    /// <summary>身份键（写入唯一写门时给出的键，须与所属域身份模板逐字同形）。</summary>
    string IdentityKey(string container, int round, string kind, long ordinal);

    /// <summary>父键引用（轮键；载荷 <c>parent</c> 字段，键引用而非对象引用）。</summary>
    string ParentKey(string container, int round);

    /// <summary>帧级显示键（读侧内存合成，非持久位置）。</summary>
    string DisplayKey(string container, int round, string kind, long ordinal);

    /// <summary>从键反解容器（单键/多键形态；不合形 ⇒ <c>false</c>，不猜）。</summary>
    bool TryParseContainer(string? key, out string container);

    /// <summary>四域身份值袋（身份占位符 → 期望值；供唯一写门反解值逐键比对）。</summary>
    IReadOnlyDictionary<string, string> ValuesOf(string container, int round, string kind, long ordinal);

    /// <summary>单键域身份值袋（身份模板仅 <c>{container}</c>）。</summary>
    IReadOnlyDictionary<string, string> ValuesOfContainer(string container);
}
