using Modules.Wiring.Model;

namespace Modules.Wiring.Compile;

/// <summary>
/// 冻结产物：Plan + 指纹 + 代际（热插拔隔离，R4）。
/// <para>旧代 plan 标 <c>dep</c> 由上层登记；本层只保证「指纹可复算 + 代际可辨」。</para>
/// </summary>
public sealed record FrozenWiringPlan(WiringPlan Plan, string Fingerprint, string Generation)
{
    public bool IsFrozen => !string.IsNullOrWhiteSpace(Fingerprint);

    public bool IsClosed => IsFrozen && Plan.IsClosed;

    /// <summary>不同代际 ⇒ 不得互投（跨代串扰防线）。</summary>
    public bool SameGeneration(FrozenWiringPlan other)
        => other is not null && string.Equals(Generation, other.Generation, StringComparison.Ordinal);
}

/// <summary>Freeze：指纹 + 代际（同源两次编译指纹相等）。</summary>
public static class WiringFreezer
{
    public const string LiveGeneration = "live";

    public static FrozenWiringPlan Freeze(WiringPlan plan, string generation = LiveGeneration)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new FrozenWiringPlan(plan, plan.Fingerprint, generation ?? LiveGeneration);
    }

    public static FrozenWiringPlan Freeze(
        WiringCatalog catalog,
        System.Collections.Generic.IReadOnlyList<CompiledWire> wires,
        string generation = LiveGeneration)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(wires);

        var plan = WiringPlan.Create(
            catalog.Fingerprint, wires, Array.Empty<Diagnostics.WiringViolation>(),
            WiringPlanner.RequiredInputsOf(catalog));
        return Freeze(plan, generation);
    }

    /// <summary>冻结产物的指纹是否仍等于当前声明源（不一致 ⇒ 陈旧 plan，禁投递）。</summary>
    public static bool IsAligned(FrozenWiringPlan frozen, WiringCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(frozen);
        ArgumentNullException.ThrowIfNull(catalog);
        return frozen.IsFrozen && string.Equals(frozen.Fingerprint, catalog.Fingerprint, StringComparison.Ordinal);
    }
}
