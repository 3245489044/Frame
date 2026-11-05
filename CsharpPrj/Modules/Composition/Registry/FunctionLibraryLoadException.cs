using System;

namespace Modules.Registry;

/// <summary>
/// 单个函数库装配失败（配置/程序集/期望清单/注册项）。由 <see cref="FunctionLibraryLoader"/>
/// 捕获为 <c>LoadEntryResult</c>，再按 <c>required</c> 决定阻断或灰态降级。
/// </summary>
public sealed class FunctionLibraryLoadException : Exception
{
    public FunctionLibraryLoadException(string message)
        : base(message)
    {
    }

    public FunctionLibraryLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
