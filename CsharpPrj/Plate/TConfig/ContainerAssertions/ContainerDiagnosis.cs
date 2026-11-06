
namespace TConfig.ContainerAssertions
{
    /// <summary>
    /// 断言诊断（ContainerDiagnosis）——「定位 → 判据 → 诊断」内核的唯一结果类型（纯值，不含句柄/控件）。
    /// 设计要点：
    /// 1）<see cref="Kind"/> 机读：命令层结构化日志直接落类别，回归统计按类别聚合，不再解析中文文案；
    /// 2）<see cref="Reason"/> 是唯一文案出口（经 <see cref="ContainerFailureMessages.Format"/>），命令层不得自行拼装；
    /// 3）事实字段（Subject/Host/Expected/Actual/MountState/Candidates）随诊断一起流转，
    ///    使「宿主未解析 / 宿主已解析但控件缺失 / 孤儿 / 不可见 / 计数失配」不再被折叠成同一句话。
    /// </summary>
    public readonly record struct ContainerDiagnosis
    {
        /// <summary>判定类别（None = 成功）。</summary>
        public ContainerFailureKind Kind { get; init; }

        /// <summary>断言目标（控件名 / 容器名）。</summary>
        public string? Subject { get; init; }

        /// <summary>宿主（容器名 / 作用域）。</summary>
        public string? Host { get; init; }

        /// <summary>期望（计数表达式等）。</summary>
        public string? Expected { get; init; }

        /// <summary>实际值（计数等）。</summary>
        public int? Actual { get; init; }

        /// <summary>挂载状态（未挂载时的事实）。</summary>
        public string? MountState { get; init; }

        /// <summary>诊断证据：宿主/查找范围内现有子控件名样本。</summary>
        public IReadOnlyList<string>? Candidates { get; init; }

        public bool IsSuccess => Kind == ContainerFailureKind.None;

        /// <summary>构造诊断（唯一入口；null 的事实字段保持 null，不做默认值填充，避免伪造证据）。</summary>
        public static ContainerDiagnosis Of(
            ContainerFailureKind kind,
            string? subject = null,
            string? host = null,
            string? expected = null,
            int? actual = null,
            string? mountState = null,
            IReadOnlyList<string>? candidates = null)
            => new()
            {
                Kind = kind,
                Subject = subject,
                Host = host,
                Expected = expected,
                Actual = actual,
                MountState = mountState,
                Candidates = candidates,
            };

        /// <summary>成功诊断（可带目标与实测值，供成功日志使用）。</summary>
        public static ContainerDiagnosis Success(string? subject = null, int? actual = null)
            => Of(ContainerFailureKind.None, subject, actual: actual);

        /// <summary>按命令前缀渲染唯一失败文案（成功返回空串）。</summary>
        public string Reason(string? commandPrefix = null)
        {
            if (IsSuccess) return string.Empty;

            // 主/从参数按类别归位：容器级失败的主语是「宿主」，控件级失败的主语是「控件」且宿主作附属。
            // 不做这层归位会把宿主名与控件名错位传入（如「未找到宿主容器 'chainLabel'」），制造新的文案假信息。
            var (primary, hostArg) = Kind switch
            {
                ContainerFailureKind.HostNotResolved
                    or ContainerFailureKind.HostNoControl
                    or ContainerFailureKind.HostNotMounted
                    or ContainerFailureKind.HostSameAsTarget
                    or ContainerFailureKind.NotAContainer
                    or ContainerFailureKind.CountAssertionFailed
                    => (Host ?? Subject, (string?)null),
                _ => (Subject, Host),
            };

            var body = ContainerFailureMessages.Format(Kind, primary, hostArg, Expected, Actual, MountState, Candidates);
            return string.IsNullOrEmpty(commandPrefix) ? body : $"{commandPrefix}: {body}";
        }

        public override string ToString() => IsSuccess ? "OK" : Kind.ToString();
    }
}
