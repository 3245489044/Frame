#nullable enable

namespace FrameworkAbstractions.WinFormsAbstractions;

/// <summary>
/// 加载渲染选项（Phase B·P1 根因）：load/load-config-show 委托 <see cref="IConfigLoadRenderService"/> 时的入参。
/// <see cref="Show"/> 来自命令 modifier=show 的解析（load-config-show 模板置 true）。
/// </summary>
public sealed record LoadRenderOptions
{
    /// <summary>是否展示窗体（来自命令 modifier=show）。缺省 true。</summary>
    public bool Show { get; init; } = true;
}

/// <summary>
/// 加载渲染结果（Phase B·P1 根因）：结构化回传，命令据此 SetSuccess/SetFailure（保留 ResultValue 向后兼容）。
/// </summary>
public sealed class LoadRenderResult
{
    public bool Succeeded { get; set; }

    public string Scope { get; set; } = "";

    public string ConfigPath { get; set; } = "";

    public string? Error { get; set; }

    public int NodeCount { get; set; }

    public int HostChildCount { get; set; }

    /// <summary>只读退化路径返回的原始配置文本（向后兼容旧 load 返回 JSON 文本）。</summary>
    public string? ConfigText { get; set; }

    /// <summary>渲染结果摘要（写回 CommandResultComponent.ResultValue）。</summary>
    public string Summary =>
        Succeeded
            ? $"rendered scope={Scope}, nodeCount={NodeCount}, hostChildCount={HostChildCount}"
            : $"failed scope={Scope}: {Error}";
}

/// <summary>
/// 「加载即渲染」唯一权威服务（Phase B·P1 根因）：把独立配置（如 ClineBrowser）真正渲染进 ECS 渲染世界，
/// 使命令 get/set/click 可命中真实控件（此前 load 仅读文件返回 JSON 文本，modifier=show 被忽略）。
/// 接口放共享层（BackEndDriven/FrontEndDriven 均引用），实现为 FrontEndDriven.ConfigLoadRenderService
/// （经 AppServiceProvider 解析）——与 IEcsStateAccessor/IScopeContext 同构。
/// </summary>
public interface IConfigLoadRenderService
{
    /// <summary>
    /// 加载并展示配置为独立 scope（含宿主窗体创建/绑定/渲染，见实施文档 §5.1）。
    /// 同步桥：阻塞等待渲染完成（命令管线在非 UI 线程，内部 UI 工作经 IUIDispatcher 封送）。
    /// </summary>
    LoadRenderResult LoadAndShow(string configPath, LoadRenderOptions? options = null);
}
