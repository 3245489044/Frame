using System;
using System.Threading.Tasks;

namespace Share.HostAssembly.Errors;

/// <summary>
/// 通用错误分类枚举 —— 与具体业务错误类型解耦的可移植分类。
/// 领域可基于此枚举做更细的子分类或映射（如熔断等级、异常类型），框架侧仅承诺分类动作。
/// </summary>
public enum ErrorCategory
{
    /// <summary>解析/格式错误。</summary>
    Parse,
    /// <summary>编译错误。</summary>
    Compilation,
    /// <summary>执行失败（业务/运行时抛异常）。</summary>
    Execution,
    /// <summary>超时。</summary>
    Timeout,
    /// <summary>部分输出（未完整但不为空）。</summary>
    PartialOutput,
    /// <summary>空结果。</summary>
    EmptyResult,
    /// <summary>资源耗尽（限流/配额/系统资源）。</summary>
    ResourceExhausted,
    /// <summary>不可恢复（须停机）。</summary>
    Irrecoverable,
    /// <summary>未识别分类。</summary>
    Unknown,
}
