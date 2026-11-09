#nullable enable

using System;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.Abstractions;
using FrontEndDriven.Diagnostics;
using Share.Conversation.Rendering.Surface;
using Share.Conversation.Rendering.Surface.Selection;
using Share.SurfacePaint.Compat;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 外观档切换（**唯一入口**）：校验声明档存在 → 装配 → 验证 → 提交选择源 → 通知消费方 → 落盘。
/// <para>启动装配与运行期换档走同一条路径；「当前档」只有 <see cref="SurfaceSelectionProvider"/> 一份。</para>
/// <para>原子性：提交前失败 ⇒ 回装旧档并恢复选择源，且**不落盘**；只有用户主动切换且事务成功才写盘。</para>
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
    /// 应用指定档。
    /// <para>档解析：<paramref name="profileId"/> 非空 ⇒ 用它；为空时，用户主动切换（<paramref name="persistSelection"/>）
    /// 读草稿 <c>panels.profile</c>，启动装配读已提交 <c>surface.profile</c>。</para>
    /// <para>显式档不存在 ⇒ false 且不改现状；请求档失效 ⇒ 记日志后回退声明 <c>active_profile</c>（启动不因陈旧偏好而不装配）。</para>
    /// </summary>
    /// <param name="program">表面声明。</param>
    /// <param name="profileId">目标档（null/空 ⇒ 按来源读持久化档）。</param>
    /// <param name="carrierId">载体 id。</param>
    /// <param name="persistSelection">是否为用户主动切换（true 才会在成功后写盘）。</param>
    public static bool Apply(
        SurfaceProgramDeclaration program,
        string? profileId = null,
        string carrierId = "winforms",
        bool persistSelection = false)
    {
        if (program is null || !program.IsDeclared) return false;

        var fromRequest = string.IsNullOrWhiteSpace(profileId);
        var target = fromRequest
            ? (persistSelection
                ? SurfaceQualitySettings.RequestedProfile()
                : SurfaceQualitySettings.CommittedProfile())
            : profileId!.Trim();
        var view = SurfaceProgram.From(program);
        if (string.IsNullOrWhiteSpace(target) || view.EffectiveProfile(target) is null)
        {
            if (!fromRequest)
            {
                FrontEndDrivenLog.Log("SurfaceAppearanceSwitch_unknown_profile", new { profile = target, carrier = carrierId });
                return false;
            }

            FrontEndDrivenLog.Log("SurfaceAppearanceSwitch_requested_profile_invalid", new
            {
                profile = target,
                carrier = carrierId,
                fallback = program.ActiveProfile
            });
            target = program.ActiveProfile;
            if (view.EffectiveProfile(target) is null)
            {
                FrontEndDrivenLog.Log("SurfaceAppearanceSwitch_no_profile", new
                {
                    carrier = carrierId,
                    declared = program.ActiveProfile
                });
                return false;
            }
        }

        lock (Gate)
        {
            // 1) snapshot：回滚只依赖这几个值，commit 段之前不许前移。
            var previousProgram = _currentProgram;
            var previousCarrier = _currentCarrier;
            var previousProfile = _currentProgram is { IsDeclared: true }
                ? SurfaceSelectionProvider.Current.ProfileId
                : null;

            // 2) Install（不预先改写选择源，避免失败留下半态）。
            var installed = SkinAppearanceInstaller.Install(program, target, carrierId);

            // 3) 验证：安装返回值 + 装配记录必须同时指向目标档。
            var verified = installed
                && string.Equals(SkinDiagnostics.Current.Profile, target, StringComparison.Ordinal);
            if (!verified)
            {
                RollbackLocked(previousProgram, previousProfile, previousCarrier, target, carrierId);
                FrontEndDrivenLog.Log("SurfaceAppearanceSwitch_commit_failed", new
                {
                    target,
                    carrier = carrierId,
                    stage = installed ? "verify" : "install",
                    previous = previousProfile
                });
                return false;
            }

            // 4) commit：选择源与「已装配声明」一起前移。
            SurfaceSelectionProvider.Current.ProfileId = target;
            _currentProgram = program;
            _currentCarrier = carrierId;
        }

        // 5) 通知消费方失效缓存；异常只记录，不回滚已提交结果。
        RaiseApplied(target);

        // 6) 只有用户主动切换且事务成功才落盘。
        if (persistSelection)
        {
            var store = AppServiceProvider.Resolve<IUiLayoutStateStore>();
            store?.SetSurfaceSelection(target, program.Fingerprint);
            store?.Flush();
        }

        return true;
    }

    /// <summary>按已安装声明 + 持久化草稿档重新应用（画质面板「应用 / 恢复默认」的唯一路径，成功后落盘）。</summary>
    public static bool ReapplyRequested()
        => _currentProgram is { } program && Apply(program, null, _currentCarrier, persistSelection: true);

    /// <summary>
    /// 启动装配：只用**已提交**档（<c>surface.profile</c>，旧文件兼容 <c>panels.profile</c>），
    /// 缺省回落声明 <c>active_profile</c>；**不写盘**。
    /// </summary>
    public static bool ApplyCommitted(
        SurfaceProgramDeclaration program,
        string carrierId = "winforms")
        => Apply(program, null, carrierId, persistSelection: false);

    /// <summary>失败回滚：有旧档则回装旧档，否则卸载；选择源恢复到旧值。调用方必须持有 <see cref="Gate"/>。</summary>
    private static void RollbackLocked(
        SurfaceProgramDeclaration? previousProgram,
        string? previousProfile,
        string previousCarrier,
        string target,
        string carrierId)
    {
        var hasPrevious = previousProgram is { IsDeclared: true } && !string.IsNullOrWhiteSpace(previousProfile);
        if (!hasPrevious)
        {
            SkinAppearanceInstaller.Uninstall();
            SurfaceSelectionProvider.Current.ProfileId = SurfaceSelectionProvider.DefaultProfile;
            return;
        }

        var restored = SkinAppearanceInstaller.Install(previousProgram!, previousProfile!, previousCarrier);
        if (restored)
        {
            SurfaceSelectionProvider.Current.ProfileId = previousProfile!;
            return;
        }

        SkinAppearanceInstaller.Uninstall();
        SurfaceSelectionProvider.Current.ProfileId = SurfaceSelectionProvider.DefaultProfile;
        FrontEndDrivenLog.Log("SurfaceAppearanceSwitch_rollback_failed", new
        {
            target,
            previous = previousProfile,
            carrier = carrierId,
            error = "install-previous-returned-false"
        });
    }

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
