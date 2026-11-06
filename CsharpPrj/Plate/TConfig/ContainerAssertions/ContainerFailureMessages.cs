
namespace TConfig.ContainerAssertions
{
    /// <summary>
    /// 容器解析/挂载判定失败类别（R3：统一失败文案源）。
    /// 供 ContainerAssertionService（assert-container/validate-mount/validate）、
    /// 等命令层/断言/投影共用同一判定语义与文案格式化，消除「宿主未解析/未挂载」在各处各自拼装的文字漂移。
    /// 纯值类型：不承载强类型句柄（ContainerHandle 权威在 FrameworkAbstractions.ContainerAbstractions），
    /// 避免制造第三套并行句柄类型；仅承载「判定类别 + 文案格式化」。
    /// 类别即机读口径：命令层结构化日志直接落 Kind，回归统计按类别聚合，不再靠解析中文文案。
    /// </summary>
    public enum ContainerFailureKind
    {
        /// <summary>判定成功（无失败）。</summary>
        None,

        /// <summary>容器访问端口未注册（框架装配缺失，非容器判定失败）。</summary>
        AccessPortUnavailable,

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

        /// <summary>控件不在宿主控件树内（宿主已解析、目标名在宿主树内不存在）。</summary>
        ControlNotInHostTree,

        /// <summary>控件存在但未挂载到任何窗体树（孤儿控件）。</summary>
        OrphanControl,

        /// <summary>控件已挂载但不可见（Visible=false）。</summary>
        ControlNotVisible,

        /// <summary>配置加载失败。</summary>
        ConfigLoadFailed,

        /// <summary>渲染管线未成功（挂载跳过）。</summary>
        RenderNotSucceeded,

        /// <summary>宿主未收到子控件（宿主物理挂载断链）。</summary>
        HostEmptyAfterRender,

        /// <summary>目标名不是容器宿主（该名字属于其他命名空间：控件/作用域/窗体/配置/探测函数）。</summary>
        NotAContainer,
    }

    /// <summary>
    /// 容器挂载失败文案的唯一格式化源（R3）。
    /// 命令层与断言经 <see cref="Format"/> 读取同一文案，改文案只改此处，杜绝文字漂移。
    /// 「宿主已解析但目标控件不在树内」会附带宿主现有子控件样本（诊断证据），
    /// 使「脚本断言了不存在的控件」一眼可辨，无需再去推断是不是宿主解析断链。
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
        /// <param name="candidates">诊断证据：宿主/查找范围内现有子控件名样本（可空）。</param>
        public static string Format(
            ContainerFailureKind kind,
            string? target = null,
            string? host = null,
            string? countExpr = null,
            int? actual = null,
            string? mountState = null,
            IReadOnlyList<string>? candidates = null)
        {
            var t = target ?? string.Empty;
            var text = kind switch
            {
                ContainerFailureKind.None => string.Empty,
                ContainerFailureKind.AccessPortUnavailable => "容器访问端口未注册（IContainerAccessPort），无法判定容器/挂载（框架装配缺失）",
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
                ContainerFailureKind.OrphanControl => $"控件 '{t}' 存在但未挂载到任何可见窗体树（孤儿控件）",
                ContainerFailureKind.ControlNotVisible => $"控件 '{t}' 不可见（Visible=false）",
                ContainerFailureKind.ConfigLoadFailed => $"配置加载失败: {t}",
                ContainerFailureKind.RenderNotSucceeded => "渲染管线未成功（挂载跳过）",
                ContainerFailureKind.HostEmptyAfterRender => "宿主未收到子控件（宿主物理挂载断链）",
                ContainerFailureKind.NotAContainer => $"'{t}' 不是容器宿主（该名字属于其他命名空间：控件/作用域/窗体/配置/探测函数）",
                _ => string.Empty,
            };

            // 只有「已解析到宿主、只是目标名不在树内」才附候选证据；宿主未解析时附候选无意义。
            if (kind == ContainerFailureKind.ControlNotInHostTree && candidates is { Count: > 0 })
                text += $"（宿主内可到达控件: {string.Join(", ", candidates)}）";

            return text;
        }
    }
}
