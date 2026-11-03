using System;

namespace Operation.HierarchyMethod.Tasks.Orchestration;

/// <summary>
/// 编排动作词表端口（声明面由宿主装载）：框架只做判定调用，不认 uhar/平台编译产物。
/// <para>未装配校验器 ⇒ 不拦截（迁移期/纯内存宿主）；装配后由宿主词表唯一裁决。</para>
/// </summary>
public static class OrchestrationOpsVocabulary
{
    /// <summary>声明面文件名（宿主装载的单一真源）。</summary>
    public const string DeclarationFileName = "orchestration-ops.ushar";

    /// <summary>词表空间号。</summary>
    public const string SpaceId = "OrchestrationOp";

    /// <summary>词表成员判定器（宿主注册；null = 不拦截）。</summary>
    public static Func<string, bool>? Validator { get; set; }

    public static bool IsDeclaredOp(string? op)
        => Validator?.Invoke(op ?? string.Empty) ?? true;
}
