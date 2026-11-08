#nullable enable

using System;
using System.Windows.Forms;
using FrontEndDriven.WinForms.Applier;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// C 类样式桥：把旧样式系统的 <c>BackColor</c>（<c>#F0F0F0</c> / <c>#2563EB</c> / <c>#2D2D2D</c> / <c>#3C3C3C</c>）
/// 收编为「表面角色」而不是第二写者。
/// <para>AccentTint → 只作为 tint 叠在材质面上；NeutralHint / Ignore → 不写；DirectColor → 保留业务色。</para>
/// </summary>
public static class SurfaceStyleBridge
{
    /// <summary>accent 叠加强度（旧样式色只做提示，不覆盖玻璃面；非新色值）。</summary>
    private const int AccentTintAlpha = 0x26;

    /// <summary>样式色在表面体系中的角色（族声明是唯一真源；未登记 ⇒ 保留业务色）。</summary>
    /// <param name="desc">控件描述符（族未传入时按 <see cref="ControlDescriptor.Type"/> 回查登记表）。</param>
    /// <param name="family">已登记族（可空）。</param>
    public static SurfaceStyleRole RoleFor(ControlDescriptor desc, SurfaceControlFamilySpec? family)
        => (family ?? SurfaceControlFamilyRegistry.For(desc.Type))?.StyleRole ?? SurfaceStyleRole.DirectColor;

    /// <summary>AccentTint 的 tint ARGB（其余角色 ⇒ null）。</summary>
    /// <param name="desc">控件描述符。</param>
    /// <param name="role">样式角色。</param>
    public static int? TintFor(ControlDescriptor desc, SurfaceStyleRole role)
    {
        if (role != SurfaceStyleRole.AccentTint || desc.BackColor is not { Length: > 0 } text) return null;
        if (!UiBackend.SurfaceColor.TryParseColor(text, out var argb)) return null;
        if (((argb >> 24) & 0xFF) == 0) return null;
        return (argb & 0x00FFFFFF) | (AccentTintAlpha << 24);
    }

    /// <summary>是否应剥离声明 <c>BackColor</c>（唯一写者归材质通道）。</summary>
    /// <param name="desc">控件描述符。</param>
    /// <param name="control">目标控件。</param>
    public static bool SuppressStyleBackColor(ControlDescriptor desc, Control control)
    {
        var family = SurfaceControlFamilyRegistry.ForControl(control, desc.Type);
        var role = RoleFor(desc, family);
        if (role == SurfaceStyleRole.DirectColor) return false;
        return family?.OwnsBackColor ?? SkinDecorator.IsPropertyChannelOwned(control);
    }
}
