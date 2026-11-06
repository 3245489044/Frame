using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.Container;
using Modules.Parsing.Invocation;
using Share.EventBindingOrchestrator.FunctionHub;

namespace FrontEndDriven.State.Projection;

/// <summary>
/// 调用面命名空间来源装配（唯一入口）：把「运行期真实可寻址的名字」注册进
/// <see cref="NameSpaceRegistry"/>，供加载期校验器判定「这个名字属不属于该空间」，
/// 而不是跑到第 N 条命令才以 HostNotResolved / 控件不存在收场。
/// <para>判据载体 = 注册表/控件池/函数表/窗体集合（结构事实），非文本出现与否。</para>
/// <para>来源优先级：Declared（清单，无 UI 可复算）&lt; Live（本装配，覆盖清单）。</para>
/// </summary>
public static class InvocationNameSpaceProbes
{
    private static int _registered;

    /// <summary>
    /// 注册六类空间来源。缺失的依赖（如未装配 IControlFinder / IFunctionHub）⇒ 该空间保持
    /// 「无来源」，校验器按 Unavailable 跳过（不伪造 NotInSpace）。
    /// </summary>
    /// <param name="hostMount">容器挂载服务（ContainerHost / Scope 的事实源）。</param>
    /// <param name="controlFinder">控件按名查找器（Control 的事实源）。</param>
    /// <param name="configFileExists">配置文件存在性谓词（由组合根注入，避免本类依赖路径服务）。</param>
    /// <param name="configFileNames">配置文件枚举（可空）。</param>
    public static void Register(
        HostMountService hostMount,
        IControlFinder? controlFinder,
        Func<string, bool>? configFileExists = null,
        Func<IEnumerable<string>>? configFileNames = null)
    {
        ArgumentNullException.ThrowIfNull(hostMount);

        // 容器宿主：只认 ContainerRegistry 已登记的 scope/hostKey（视图名/面板名不在其中）。
        NameSpaceRegistry.Register(new DelegateNameSpaceSource(
            NameSpaceId.ContainerHost,
            name => hostMount.ResolveByScopeOrHostKey(name) != null));

        NameSpaceRegistry.Register(new DelegateNameSpaceSource(
            NameSpaceId.Scope,
            name => hostMount.ResolveByScope(name) != null));

        // 控件：可见树/控件池按名解析（与 validate 同口径）。
        if (controlFinder is not null)
        {
            NameSpaceRegistry.Register(new DelegateNameSpaceSource(
                NameSpaceId.Control,
                name => controlFinder.FindByName<Control>(name) != null));
        }

        // 探测函数：函数目录已注册名（wait-until/exec 的目标）。
        var hub = AppServiceProvider.Resolve<IFunctionHub>();
        if (hub is not null)
        {
            NameSpaceRegistry.Register(new DelegateNameSpaceSource(
                NameSpaceId.ProbeFunction,
                hub.HasFunction,
                NameSpaceOrigin.Live,
                hub.GetWorkflowNames));
        }

        // 配置文件：由组合根注入路径事实（本类不依赖 Route/InformationPaths）。
        if (configFileExists is not null)
        {
            NameSpaceRegistry.Register(new DelegateNameSpaceSource(
                NameSpaceId.ConfigFile,
                configFileExists,
                NameSpaceOrigin.Live,
                configFileNames));
        }

        // 窗体：已打开窗体名（show/close 的目标）。
        NameSpaceRegistry.Register(new DelegateNameSpaceSource(
            NameSpaceId.Form,
            name => Application.OpenForms.Cast<Form>()
                .Any(form => string.Equals(form.Name, name, StringComparison.OrdinalIgnoreCase)),
            NameSpaceOrigin.Live,
            () => Application.OpenForms.Cast<Form>().Select(form => form.Name)));

        _registered = 1;
    }

    /// <summary>是否已完成装配（诊断用）。</summary>
    public static bool IsRegistered => _registered == 1;
}
