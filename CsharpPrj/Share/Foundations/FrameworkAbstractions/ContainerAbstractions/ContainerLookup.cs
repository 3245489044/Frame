
#nullable enable

using System.Windows.Forms;
using TConfig.ContainerAssertions;

namespace FrameworkAbstractions.ContainerAbstractions;

/// <summary>
/// 容器/控件定位结果（ContainerLookup）——「定位」与「判据」之间的显式边界。
/// 存在的理由：旧实现用 <c>(Control?, ContainerId?)</c> 元组表达定位结果，
/// 把「宿主未解析」与「宿主已解析但目标名不在树内」折叠成同一个 <c>control == null</c>，
/// 导致断言层只能统一报「未找到宿主容器」，把脚本过期（目标控件不存在）误诊为挂载断链。
/// 本类型强制区分每一类失败（<see cref="Kind"/>），并携带诊断证据（<see cref="Candidates"/>），
/// 供 <see cref="ContainerDiagnosis"/> 单向下转换，杜绝再次折叠。
/// </summary>
public sealed record ContainerLookup
{
    /// <summary>定位类别（None = 命中）。</summary>
    public ContainerFailureKind Kind { get; init; } = ContainerFailureKind.None;

    /// <summary>命中的目标控件（Kind=OrphanControl 时也携带对象本身，便于区分「对象不存在」与「未挂载」）。</summary>
    public Control? Control { get; init; }

    /// <summary>命中的宿主句柄（宿主内定位时非空）。</summary>
    public ContainerHandle? Host { get; init; }

    /// <summary>目标名（控件名）。</summary>
    public string? Subject { get; init; }

    /// <summary>宿主标识（作用域或 hostKey，未指定宿主时为空）。</summary>
    public string? HostKey { get; init; }

    /// <summary>诊断证据：宿主现有子控件名样本（目标不在宿主树内时填充）。</summary>
    public IReadOnlyList<string>? Candidates { get; init; }

    /// <summary>是否定位成功（控件已就位）。</summary>
    public bool IsFound => Kind == ContainerFailureKind.None && Control != null;

    /// <summary>转诊断（定位事实原样上抛，不猜、不合并类别）。</summary>
    public ContainerDiagnosis ToDiagnosis()
        => ContainerDiagnosis.Of(Kind, Subject, HostKey, actual: null, candidates: Candidates);
}
