using System;

namespace Operation.HierarchyMethod.Center;

/// <summary>
/// 运行时「单一确定性写者」断言（SEP.DeterministicCoreDisjoint 的运行时侧；编译期静态扫描 = Check-SingleWriter.ps1）。
/// 确定性核心（ur_nodes / KeyMapping 分类写 / 图权威边）只允许经唯一注册的写路径进入——平行实现/越权写在此 fail-fast。
/// 本类为 <c>Modules.Threading.SingleWriterRegistry</c> 的**薄门面**（单一写者断言逻辑唯一权威归 Modules.Threading）。
/// </summary>
public static class SingleWriteGuard
{
    /// <summary>注册某写目标的唯一写者。已有不同写者 → 抛 <see cref="InvalidOperationException"/>（越权写 fail-fast，不静默分叉）。</summary>
    public static void AssertSingleWriter(string target, string writer)
        => Modules.Threading.SingleWriterRegistry.AssertSingleWriter(target, writer);

    /// <summary>释放写目标（供测试重置 / 组合根重建）。仅注册写者可释放；未注册/非本写者 no-op。</summary>
    public static void Release(string target, string writer)
        => Modules.Threading.SingleWriterRegistry.Release(target, writer);
}
