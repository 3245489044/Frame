using Modules.Wiring.Abstractions;
using Modules.Wiring.Model;

namespace Modules.Wiring.Declaration;

/// <summary>内存声明来源（JSON / ushar 适配器 / 测试的统一落点；只读）。</summary>
public sealed record WiringDeclaration(
    string SourceId,
    IReadOnlyList<FormSpec> Forms,
    IReadOnlyList<BoxSpec> Boxes,
    IReadOnlyList<WireSpec> Wires,
    IReadOnlyList<AdapterSpec> Adapters,
    IReadOnlyList<ProfileSpec> Profiles) : IWiringDeclarationSource
{
    public static WiringDeclaration Empty { get; } = new(
        string.Empty,
        Array.Empty<FormSpec>(),
        Array.Empty<BoxSpec>(),
        Array.Empty<WireSpec>(),
        Array.Empty<AdapterSpec>(),
        Array.Empty<ProfileSpec>());

    /// <summary>增量追加（构造期唯一入口；不修改既有实例）。</summary>
    public WiringDeclaration With(
        IEnumerable<FormSpec>? forms = null,
        IEnumerable<BoxSpec>? boxes = null,
        IEnumerable<WireSpec>? wires = null,
        IEnumerable<AdapterSpec>? adapters = null,
        IEnumerable<ProfileSpec>? profiles = null,
        string? sourceId = null)
        => new(
            sourceId ?? SourceId,
            forms is null ? Forms : Forms.Concat(forms).ToArray(),
            boxes is null ? Boxes : Boxes.Concat(boxes).ToArray(),
            wires is null ? Wires : Wires.Concat(wires).ToArray(),
            adapters is null ? Adapters : Adapters.Concat(adapters).ToArray(),
            profiles is null ? Profiles : Profiles.Concat(profiles).ToArray());
}
