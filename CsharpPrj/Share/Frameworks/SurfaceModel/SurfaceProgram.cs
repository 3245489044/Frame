#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Modules.CapabilityResolution;
using Share.Conversation.Rendering.Surface;

namespace Share.SurfaceModel;

/// <summary>
/// 表面程序门面（跨产品）：包装 <see cref="SurfaceProgramDeclaration"/>，把能力矩阵/视图索引一并收口。
/// <para>门面不复制解析逻辑；程序不可用 ⇒ <see cref="Empty"/>（可见降级，禁伪造默认外观）。</para>
/// </summary>
public sealed class SurfaceProgram
{
    /// <summary>空程序（未装配）：视图/容器一律缺失，解析方走可见降级。</summary>
    public static SurfaceProgram Empty { get; } = new(SurfaceProgramDeclaration.Empty);

    private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> _carrierCaps;
    private readonly Lazy<CapabilityMatrix> _capabilities;

    private SurfaceProgram(SurfaceProgramDeclaration declaration)
    {
        Declaration = declaration ?? throw new ArgumentNullException(nameof(declaration));
        _carrierCaps = new Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(
            BuildCarrierCaps,
            isThreadSafe: true);
        _capabilities = new Lazy<CapabilityMatrix>(
            () => CapabilityMatrix.FromStrings(_carrierCaps.Value),
            isThreadSafe: true);
    }

    /// <summary>由既有声明模型构造门面（同一快照，不重新解析）。</summary>
    /// <param name="declaration">声明模型。</param>
    public static SurfaceProgram From(SurfaceProgramDeclaration declaration)
        => new(declaration ?? throw new ArgumentNullException(nameof(declaration)));

    /// <summary>解析生成物文本（哨兵不符/不可解析 ⇒ 空程序）。</summary>
    /// <param name="json">生成物文本。</param>
    public static SurfaceProgram Parse(string json)
        => From(SurfaceProgramDeclaration.Parse(json));

    /// <summary>底层声明模型（既有解析器的唯一入参）。</summary>
    public SurfaceProgramDeclaration Declaration { get; }

    /// <summary>是否来自可用声明。</summary>
    public bool IsDeclared => Declaration.IsDeclared;

    /// <summary>声明指纹（缓存/漂移判据）。</summary>
    public string Fingerprint => Declaration.Fingerprint;

    /// <summary>视图 → 容器键（结构序）。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Views => Declaration.Views;

    /// <summary>容器（view:key → 规格）。</summary>
    public IReadOnlyDictionary<string, SurfaceContainerSpec> Containers => Declaration.Containers;

    /// <summary>结构映射（id → 规格）。</summary>
    public IReadOnlyDictionary<string, SurfaceMappingSpec> Mappings => Declaration.Mappings;

    /// <summary>层（name → 规格）。</summary>
    public IReadOnlyDictionary<string, SurfaceLayerSpec> Layers => Declaration.Layers;

    /// <summary>材质（name → 规格）。</summary>
    public IReadOnlyDictionary<string, SurfaceMaterialSpec> Materials => Declaration.Materials;

    /// <summary>外观档（name → 规格）。</summary>
    public IReadOnlyDictionary<string, SurfaceProfileSpec> Profiles => Declaration.Profiles;

    /// <summary>载体（id → 规格）。</summary>
    public IReadOnlyDictionary<string, SurfaceCarrierSpec> Carriers => Declaration.Carriers;

    /// <summary>默认外观档（声明 <c>surface_setting_active_profile</c>；未声明 ⇒ 空）。</summary>
    public string ActiveProfile => Declaration.ActiveProfile;

    /// <summary>载体 → 图元 → 声明词（能力矩阵唯一输入）。</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> CarrierCapabilities => _carrierCaps.Value;

    /// <summary>能力矩阵（三态查询；声明派生，构造后缓存，禁每次访问重建）。</summary>
    public CapabilityMatrix Capabilities => _capabilities.Value;

    /// <summary>展开外观档（未声明 ⇒ null）。</summary>
    /// <param name="profileId">档名。</param>
    public SurfaceProfileSpec? EffectiveProfile(string? profileId)
        => string.IsNullOrWhiteSpace(profileId) ? null : Declaration.EffectiveProfile(profileId!);

    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> BuildCarrierCaps()
    {
        var map = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var carrier in Declaration.Carriers)
        {
            map[carrier.Key] = carrier.Value.Caps;
        }

        return map;
    }
}
