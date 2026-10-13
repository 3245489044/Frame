
namespace BackEndDriven.Execution;

/// <summary>
/// 已废弃（合并至唯一异常出口）：本类保留为空壳以防旧引用编译中断，
/// 不再注册任何进程级异常订阅（全库异常订阅点各 ≤ 1）。
/// 唯一异常出口 = Common.Diagnostics.ExceptionGateway（记录取证 + 停驻信号，绝不 Environment.Exit）。
/// </summary>
public static class GlobalExceptionHandler
{
    public static void Configure(Action<Exception>? onAppDomainException = null)
    {
    }
}
