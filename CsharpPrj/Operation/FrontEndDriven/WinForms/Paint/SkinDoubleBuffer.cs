#nullable enable

using System;
using System.Reflection;
using System.Windows.Forms;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 双缓冲启用（owner-draw 前置）：<see cref="Control.SetStyle"/> 与 <c>DoubleBuffered</c> 均为受保护成员，
/// 经受控反射启用；失败 ⇒ 返回 false + 结构原因（禁静默）。
/// </summary>
public static class SkinDoubleBuffer
{
    private static readonly MethodInfo? SetStyleMethod = typeof(Control).GetMethod(
        "SetStyle",
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        types: new[] { typeof(ControlStyles), typeof(bool) },
        modifiers: null);

    private static readonly PropertyInfo? DoubleBufferedProperty = typeof(Control).GetProperty(
        "DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Enabled = new();
    private static readonly object EnabledMarker = new();

    /// <summary>尝试启用双缓冲（幂等；已启用 ⇒ 直接成功，不再重复反射）；失败 ⇒ <c>false</c> 并给出原因码。</summary>
    /// <param name="control">目标控件（null ⇒ 失败原因 <c>control-null</c>）。</param>
    /// <param name="reason">失败原因码（成功 ⇒ null）。</param>
    public static bool TryEnable(Control? control, out string? reason)
    {
        reason = null;
        if (control is null)
        {
            reason = "control-null";
            return false;
        }

        if (Enabled.TryGetValue(control, out _)) return true;

        if (SetStyleMethod is null || DoubleBufferedProperty is null)
        {
            reason = "reflection-unavailable";
            return false;
        }

        try
        {
            SetStyleMethod.Invoke(
                control,
                new object[] { ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true });
            DoubleBufferedProperty.SetValue(control, true);
            Enabled.Add(control, EnabledMarker);
            return true;
        }
        catch (Exception ex)
        {
            reason = "reflection-failed:" + ex.GetType().Name;
            return false;
        }
    }
}
