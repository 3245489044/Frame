#nullable enable

namespace Share.Composition.Text;

/// <summary>
/// 段值四态 —— 替代 <c>string</c> 的 <c>null</c>/<c>""</c>/键缺失 三义承载四义。
/// <para>
/// <see cref="Empty"/>（已知为空，可空声明确认后合法）与 <see cref="NotApplicable"/>（本步不存在）
/// 必须分开：前者是「产出了空」，后者是「不该产出」，两者在契约判定与可见化上结论相反。
/// <see cref="Unknown"/> 是「应有但未解析出」，必须可见化（对齐 AX.NoSilentOutlet），不得压平成空串。
/// </para>
/// </summary>
public enum TextState
{
    /// <summary>有确定文本。</summary>
    Known,

    /// <summary>显式空（是否合法由段的 AllowEmpty 裁决）。</summary>
    Empty,

    /// <summary>本步不适用（不是缺失，不计缺失）。</summary>
    NotApplicable,

    /// <summary>应有但未解析出（可见化；按需求量降级或阻断）。</summary>
    Unknown,
}
