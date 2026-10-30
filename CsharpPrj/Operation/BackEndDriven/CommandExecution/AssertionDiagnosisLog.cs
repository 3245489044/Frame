
using BackEndDriven.Diagnostics;
using TConfig.ContainerAssertions;

namespace BackEndDriven.CommandExecution;

/// <summary>
/// 断言诊断的统一汇报口（命令层唯一入口）。
/// 目的：把「失败文案 + 机读类别 + 判定事实」一次落地，避免每个命令各自拼中文、各自决定记录什么：
/// - 文案唯一来自 <see cref="ContainerDiagnosis.Reason"/>（下层 ContainerFailureMessages 单一文案源）；
/// - 日志固定带 <c>kind</c>（枚举名）与事实字段，回归统计/门禁可按类别聚合，不再解析中文；
/// - 命令层不再出现「未找到宿主容器」这类跨语义复用文案。
/// </summary>
internal static class AssertionDiagnosisLog
{
    /// <summary>记录诊断并返回可直接回写命令结果的失败文案（成功时返回空串，调用方不应在成功路径使用）。</summary>
    public static string Report(string commandName, ContainerDiagnosis diagnosis)
    {
        var reason = diagnosis.Reason(commandName);

        BackEndLog.Log("Command_Assertion_Diagnosis", new
        {
            command = commandName,
            kind = diagnosis.Kind.ToString(),
            target = diagnosis.Subject,
            host = diagnosis.Host,
            expected = diagnosis.Expected,
            actual = diagnosis.Actual,
            mountState = diagnosis.MountState,
            candidates = diagnosis.Candidates == null ? null : string.Join(",", diagnosis.Candidates),
            message = reason
        });

        return reason;
    }
}
