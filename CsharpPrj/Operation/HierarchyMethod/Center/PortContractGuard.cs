using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.HierarchyMethod.Center;

/// <summary>
/// 装配期端口契约校验（R10 canonical，fail-fast）：端口可缺席导致的同链静默分叉，在装配期拦截为显式启动失败。
/// 组合根在装配完成后调用 <see cref="Ensure"/>，缺端口立即抛错（不再让 no-op 兜底掩盖缺装配的隐性依赖）。
/// 纯装配期工具：对运行期无状态，不改变已装配组合的行为。
/// </summary>
public static class PortContractGuard
{
    /// <summary>声明一个命名端口是否已装配。value 非 null = 已装配。</summary>
    public static (string Name, bool Present) Port(string name, object? value) => (name, value != null);

    /// <summary>校验端口全集；存在缺装配端口 → 抛 <see cref="InvalidOperationException"/>（fail-fast），列明缺失名单。</summary>
    public static void Ensure(IReadOnlyCollection<(string Name, bool Present)> ports)
    {
        if (ports is null) throw new ArgumentNullException(nameof(ports));
        var missing = ports.Where(p => !p.Present).Select(p => p.Name).ToList();
        if (missing.Count == 0) return;
        throw new InvalidOperationException(
            $"装配端口契约缺失（fail-fast）：{string.Join(", ", missing)}。请补齐缺装配端口后再启用；" +
            "缺端口静默 no-op 会导致同调用链因组合根装配差异走不同分支、行为不可复现。");
    }
}
