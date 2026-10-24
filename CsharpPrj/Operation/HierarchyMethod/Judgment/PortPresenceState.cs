using Modules.Node.Ports;
using Share.Composition.Text;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>
/// 端口存在性 → 通用段状态（唯一映射）：端口判据统一以四态表述，规则内不再各写「三种 presence 各判一次」。
/// <para>
/// 空白串口径已在事实层折叠（<see cref="JudgmentFact.PresenceOf"/> + <see cref="PresenceSemantics"/>），
/// 此处只做词汇映射，不重复解释空串 —— 否则同一段又会出现第二套判空口径。
/// </para>
/// </summary>
internal static class PortPresenceState
{
    /// <summary>三态事实 → 四态语义（缺席 ⇒ 未知；显式空 ⇒ 空；有值 ⇒ 已知）。</summary>
    public static TextState ToState(this PortPresence presence) => presence switch
    {
        PortPresence.Value => TextState.Known,
        PortPresence.ExplicitNull => TextState.Empty,
        _ => TextState.Unknown,
    };
}
