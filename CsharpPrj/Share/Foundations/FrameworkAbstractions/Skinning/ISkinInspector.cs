#nullable enable

namespace FrameworkAbstractions.Skinning;

/// <summary>
/// 外观诊断/像素判定的中性读面（命令层与皮肤实现之间的唯一桥）。
/// <para>为什么需要：<c>BackEndDriven</c>（命令层）与 <c>FrontEndDriven</c>（皮肤实现）互不可引用
/// （FrontEndDriven → StateHoldDriven → BackEndDriven 已构成单向链），故以本接口做依赖倒置。</para>
/// <para>实现唯一：<c>FrontEndDriven.WinForms.Paint.SkinInspector</c>；消费方：断言命令（skin-binding / skin-pixels）。</para>
/// </summary>
public interface ISkinInspector
{
    /// <summary>装配/绑定单行描述（结构事实；键序固定，可直接作为失败证据）。</summary>
    string DescribeBinding();

    /// <summary>读单个绑定字段（未知键 ⇒ false；键受控见实现）。</summary>
    /// <param name="key">字段键（enabled/profile/carrier/catalog/layers/unresolvedTokens/attached/painted/global/fingerprint）。</param>
    /// <param name="value">字段值（字符串；数值以不变文化输出）。</param>
    bool TryGetBinding(string key, out string value);

    /// <summary>未解析色值明细（无 ⇒ 空串）。</summary>
    string DescribeUnresolved();

    /// <summary>
    /// 像素判定：控件真实绘制结果 vs 未换肤基线 / 已解析材质期望。
    /// </summary>
    /// <param name="controlName">目标控件名（唯一查找口径由实现负责）。</param>
    /// <param name="minDelta">实际像素相对基线的最小偏移（0 偏移 ⇒ 材质未落像素）。</param>
    /// <param name="tolerance">期望拟合容差（RGB 欧氏距离）。</param>
    /// <param name="evidence">单行证据（无论通过与否都写出）。</param>
    /// <returns>是否通过。</returns>
    bool TryInspectPixels(string controlName, double minDelta, int tolerance, out string evidence);
}
