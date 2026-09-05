using System;

namespace Share.HostAssembly.Errors;

/// <summary>
/// 错误分类器契约 —— 把异常（及可选的类型提示）分类为可移植的 <see cref="ErrorCategory"/>。
/// 分类逻辑唯一（纯映射，无动作）；动作域由调用方按作用域决定。
/// </summary>
public interface IErrorClassifier
{
    ErrorCategory Classify(Exception? exception, ErrorCategory? hint = null);
}
