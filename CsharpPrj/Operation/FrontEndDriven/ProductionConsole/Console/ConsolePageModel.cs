using System.Collections.Generic;
using FrontEndDriven.ProductionConsole.Assistant;

namespace FrontEndDriven.ProductionConsole.Console;

/// <summary>已物化的产物视图（渲染层只读：Url 与 Error 二选一）。</summary>
public sealed class ConsoleArtifactView
{
    public ArtifactSpec Spec { get; init; } = new();

    public ArtifactUrl Url { get; init; } = ArtifactUrl.Failed("未物化");

    /// <summary>文本/JSON 小文件的内联文本（&gt;64KB 不读，只给路径）。</summary>
    public string? Text { get; init; }
}

/// <summary>
/// 控制台页面模型（一次渲染的完整输入）：能力清单、表单值、任务列表、选中任务与产物、历史抽屉。
/// 通用渲染只依赖本模型，不感知插件。
/// </summary>
public sealed class ConsolePageModel
{
    public IReadOnlyList<CapabilityDescriptor> Capabilities { get; init; } = Array.Empty<CapabilityDescriptor>();

    public string? SelectedCapabilityId { get; init; }

    public IReadOnlyDictionary<string, object?> FormValues { get; init; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    public IReadOnlyList<JobSnapshot> Jobs { get; init; } = Array.Empty<JobSnapshot>();

    public JobSnapshot? SelectedJob { get; init; }

    public IReadOnlyList<ConsoleArtifactView> Artifacts { get; init; } = Array.Empty<ConsoleArtifactView>();

    public PagedResult<JobSnapshot>? History { get; init; }

    /// <summary>历史抽屉的当前筛选词（渲染输入框回显）。</summary>
    public string? HistoryFilter { get; init; }

    /// <summary>抽屉：none / history / graph / diag。</summary>
    public string Drawer { get; init; } = "none";

    /// <summary>结构图 SVG 原文（可信，由插件提供）。</summary>
    public string? GraphSvg { get; init; }

    /// <summary>诊断文本（抽屉 diag 用）。</summary>
    public string? DrawerText { get; init; }

    /// <summary>状态栏文本（能力可用性/探活结论等）。</summary>
    public string? StatusText { get; init; }

    /// <summary>提示/错误（表单校验失败、命令失败）。</summary>
    public string? Notice { get; init; }

    /// <summary>AI 参数助理视图（独立区域 region-assist 的唯一渲染输入）。</summary>
    public ParameterAssistantView? Assistant { get; init; }
}
