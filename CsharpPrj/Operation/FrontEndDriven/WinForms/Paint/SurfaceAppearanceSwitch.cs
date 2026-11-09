#nullable enable

using System;
using FrontEndDriven.Diagnostics;
using Share.Conversation.Rendering.Surface;
using Share.Conversation.Rendering.Surface.Selection;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 外观档切换（**唯一入口**）：校验声明档存在 → 写选择源 → 用显式档装配 → 通知消费方失效缓存。
/// <para>启动装配与运行期换档走同一条路径；「当前档」只有 <see cref="SurfaceSelectionProvider"/> 一份。</para>
/// <para>宿主反向依赖：本类不引用任何宿主程序集；宿主侧缓存失效由订阅 <see cref="Applied"/> 完成。</para>
/// </summary>
public static class SurfaceAppearanceSwitch
{
    private static readonly object Gate = new();

    /// <summary>最近一次成功装配的声明与载体（换档/重装配的唯一依据；null ⇒ 尚未装配）。</summary>
    private static SurfaceProgramDeclaration? _currentProgram;
    private static string _currentCarrier = "winforms";

    /// <summary>切档完成（参数 = 生效档）。订阅方负责失效自身缓存并重绑；异常被记录，不回滚切档结果。</summary>
    public static event Action<string>? Applied;

    /// <summary>是否已装配（未装配 ⇒ 切档/重装配明确失败，不假装生效）。</summary>
    public static bool IsInstalled => _currentProgram is { IsDeclared: true };

    /// <summary>
    /// 应用指定档（null/空 ⇒ 持久化的请求档，缺省再回退声明的 <c>active_profile</c>）。
    /// <para>显式档不存在 ⇒ false 且不改现状；持久化请求档失效 ⇒ 记日志后回退声明档（启动不因陈旧偏好而不装配）。</para>
    /// </summary>
    /// <param name="program">表面声明。</param>
    /// <param name="profileId">目标档（null/空 ⇒ 请求档）。</param>
    /// <param name="carrierId">载体 id。</param>
    public static bool Apply(
        SurfaceProgramDeclaration program,
        string? profileId = null,
        string carrierId = "winforms")
    {
        if (program is null || !program.IsDeclared) return false;

        var fromRequest = string.IsNullOrWhiteSpace(profileId);
        var target = fromRequest ? SurfaceQualitySettings.RequestedProfile() : profileId!.Trim();
        var view = SurfaceProgram.From(program);
        if (string.IsNullOrWhiteSpace(target) || view.EffectiveProfile(target) is null)
        {
            if (!fromRequest)
            {
                FrontEndDrivenLog.Log("SurfaceAppearanceSwitch_unknown_profile", new { profile = target, carrier = carrierId });
                return false;
            }

            FrontEndDrivenLog.Log("SurfaceAppearanceSwitch_requested_profile_invalid", new { profile = target, carrier = carrierId });
            target = program.ActiveProfile;
            if (view.EffectiveProfile(target) is null)
            {
                FrontEndDrivenLog.Log("SurfaceAppearanceSwitch_no_profile", new { carrier = carrierId });
                return false;
            }
        }

        lock (Gate)
        {
            SurfaceSelectionProvider.Current.ProfileId = target;
            if (!SkinAppearanceInstaller.Install(program, target, carrierId)) return false;
            _currentProgram = program;
            _currentCarrier = carrierId;
        }

        RaiseApplied(target);
        return true;
    }

    /// <summary>按已安装声明 + 持久化请求档重新应用（画质面板「应用 / 恢复默认」的唯一路径）。</summary>
    public static bool ReapplyRequested()
        => _currentProgram is { } program && Apply(program, null, _currentCarrier);

    private static void RaiseApplied(string profile)
    {
        var handlers = Applied;
        if (handlers is null) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<string>)handler)(profile);
            }
            catch (Exception ex)
            {
                FrontEndDrivenLog.Log("SurfaceAppearanceSwitch_consumer_failed", new { profile, message = ex.Message });
            }
        }
    }
}
