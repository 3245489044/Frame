
namespace TConfig.ContainerAssertions
{
    /// <summary>
    /// 容器解析/挂载判定失败类别（R3：统一失败文案源）。
    /// 供 ContainerAssertionService（assert-container/validate-mount/validate）、ContainerProjector、
    /// 等命令层/断言/投影共用同一判定语义与文案格式化，消除「宿主未解析/未挂载」在各处各自拼装的文字漂移。
    /// 纯值类型：不承载强类型句柄（ContainerHandle 权威在 FrameworkAbstractions.ContainerAbstractions），
    /// 避免制造第三套并行句柄类型；仅承载「判定类别 + 文案格式化」。
    /// </summary>
    public enum ContainerFailureKind
    {
        /// <summary>判定成功（无失败）。</summary>
        None,

        /// <summary>未找到宿主容器（scope 或 hostKey 均解析不到）。</summary>
        HostNotResolved,

        /// <summary>宿主容器已登记但无控件引用（未挂载到实际控件对象）。</summary>
        HostNoControl,

        /// <summary>宿主容器未挂载（MountState 非 Mounted）。</summary>
        HostNotMounted,

        /// <summary>宿主控件与目标控件同对象（早期宿主名覆盖残留）。</summary>
        HostSameAsTarget,

        /// <summary>无效的计数表达式。</summary>
        InvalidCountExpression,

        /// <summary>子控件数断言失败（计数不满足期望表达式）。</summary>
        CountAssertionFailed,

        /// <summary>未找到目标控件（按名）。</summary>
        TargetControlNotFound,

        /// <summary>控件不在宿主控件树内。</summary>
        ControlNotInHostTree,

        /// <summary>配置加载失败。</summary>
        ConfigLoadFailed,

        /// <summary>渲染管线未成功（挂载跳过）。</summary>
        RenderNotSucceeded,

        /// <summary>宿主未收到子控件（物理挂载断链）。</summary>
        HostEmptyAfterRender,
    }

    /// <summary>
    /// 容器挂载失败文案的唯一格式化源（R3）。
    /// 命令层与断言经 <see cref="Format"/> 读取同一文案，改文案只改此处，杜绝文字漂移。
    /// </summary>
    public static class ContainerFailureMessages
    {
        /// <summary>
        /// 按判定类别格式化为统一 failure reason（供「宿主解析结果 + 挂载判定」共用的文案源）。
        /// </summary>
        /// <param name="kind">判定类别（None 返回空串，表示成功）。</param>
        /// <param name="target">目标字符串（scope / hostKey / 控件名）。</param>
        /// <param name="host">宿主字符串（ControlNotInHostTree 附带，目标控件所属宿主）。</param>
        /// <param name="countExpr">计数断言表达式（CountAssertionFailed 专用）。</param>
        /// <param name="actual">实际值（CountAssertionFailed/HostEmptyAfterRender 的计数）。</param>
        /// <param name="mountState">挂载状态字符串（HostNotMounted 附带）。</param>
        public static string Format(
            ContainerFailureKind kind,
            string? target = null,
            string? host = null,
            string? countExpr = null,
            int? actual = null,
            string? mountState = null)
        {
            var t = target ?? string.Empty;
            return kind switch
            {
                ContainerFailureKind.None => string.Empty,
                ContainerFailureKind.HostNotResolved => $"未找到宿主容器 '{t}'",
                ContainerFailureKind.HostNoControl => $"宿主容器 '{t}' 无控件引用（未挂载到实际控件对象）",
                ContainerFailureKind.HostNotMounted => $"宿主容器 '{t}' 未挂载（MountState={mountState}）",
                ContainerFailureKind.HostSameAsTarget => $"宿主容器 '{t}' 与目标控件同对象（疑似残留名字映射）",
                ContainerFailureKind.InvalidCountExpression => $"无效的计数表达式 '{countExpr}'（支持 count>0 / count==n / count>=n / count<n）",
                ContainerFailureKind.CountAssertionFailed => $"容器 '{t}' 子控件数断言失败 — 期望 {countExpr}, 实际 {actual}",
                ContainerFailureKind.TargetControlNotFound => string.IsNullOrEmpty(t)
                    ? "未找到目标控件"
                    : $"未找到控件 '{t}'",
                ContainerFailureKind.ControlNotInHostTree => $"控件 '{t}' 不在宿主 '{host}' 的控件树内",
                ContainerFailureKind.ConfigLoadFailed => $"配置加载失败: {t}",
                ContainerFailureKind.RenderNotSucceeded => "渲染管线未成功（挂载跳过）",
                ContainerFailureKind.HostEmptyAfterRender => "宿主未收到子控件（宿主物理挂载断链）",
                _ => string.Empty,
            };
        }
    }
}