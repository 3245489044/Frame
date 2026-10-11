namespace Share.CircuitFramework.Compiler;

/// <summary>
/// 代码实体解析器（G2）：由外部（Agent/工具侧）注册一个「<see cref="Core.CodeRef"/> → 是否存在」的判定，
/// 使编译期可对「声明绑定的代码实体」与真实代码**对拍**（存在性校验，替代手工/自动之分）。
/// <para>
/// 框架零领域类型：解析器为委托注入，框架不反射、不依赖任何业务程序集（默认 null = 跳过存在性校验，
/// 仅做结构与端口类型判据）。
/// </para>
/// </summary>
public static class CodeEntityResolver
{
    /// <summary>当前解析器（null = 未注册，跳过存在性校验）。</summary>
    public static Func<Core.CodeRef, bool>? Current { get; set; }

    /// <summary>清空解析器（测试 / 门禁用）。</summary>
    public static void Clear() => Current = null;
}
