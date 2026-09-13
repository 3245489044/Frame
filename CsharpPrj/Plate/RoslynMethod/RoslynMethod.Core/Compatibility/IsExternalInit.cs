// netstandard2.0 缺少 init 访问器所需的编译器标记类型（record/init 语法依赖此类型）。
// 本工程为 netstandard2.0 单目标，故此兼容类型必需。
#if NETSTANDARD2_0

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}

#endif
