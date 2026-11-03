using System;

namespace Operation.HierarchyMethod.Tasks.Orchestration;

/// <summary>
/// 编排动作词表端口（声明面由宿主装载）：框架只做判定调用，不认 uhar/平台编译产物。
/// <para>纪律（fail-closed）：未注册校验器 ⇒ <see cref="IsDeclaredOp"/> 恒 false（不放行）；
/// 需要「缺失即报错」的装配点调用 <see cref="EnsureRegistered"/>。</para>
/// </summary>
public static class OrchestrationOpsVocabulary
{
    /// <summary>声明面文件名（宿主装载的单一真源）。</summary>
    public const string DeclarationFileName = "orchestration-ops.ushar";

    /// <summary>词表空间号。</summary>
    public const string SpaceId = "OrchestrationOp";

    private static Func<string, bool>? _validator;

    /// <summary>校验器是否已注册（装配期断言用）。</summary>
    public static bool IsRegistered => _validator is not null;

    /// <summary>注册唯一校验实现（宿主装载器在类型初始化时调用；后注册覆盖先注册）。</summary>
    public static void Register(Func<string, bool> validator)
        => _validator = validator ?? throw new System.ArgumentNullException(nameof(validator));

    /// <summary>装配断言：校验器缺失 ⇒ 立即失败（禁「不读声明也通过」）。</summary>
    public static void EnsureRegistered()
    {
        if (_validator is null)
        {
            throw new System.InvalidOperationException(
                "ORCHESTRATION_OPS_VOCABULARY_UNREGISTERED：编排动作词表校验器未注册（须先触碰 OrchestrationOpsSpecLoader 完成声明装载）。");
        }
    }

    /// <summary>清空注册（测试隔离；生产装配期不调用）。</summary>
    public static void Reset() => _validator = null;

    /// <summary>词表成员判定：未注册 / 未声明 / 空白 ⇒ false（fail-closed）。</summary>
    public static bool IsDeclaredOp(string? op)
        => _validator?.Invoke(op ?? string.Empty) ?? false;
}
