#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// UI 个性化声明（生成物 <c>ui.generated.json</c>，真源 = <c>Config\ui\ui.ushar</c>）。
/// <para>
/// 单一程序四类记录（token / slot / skin / part）→ 单一产物 → 单一解析对象；
/// 换肤、换载体元素、改状态外观都只改声明，不改代码。
/// </para>
/// <para>纪律：本类零载体字面量（载体 id 全部来自声明）；不可解析 ⇒ 可见诊断 + Empty（禁静默）。</para>
/// </summary>
public sealed partial class UiDeclaration
{
    /// <summary>产物哨兵。</summary>
    public const string Schema = "ui-v1";

    /// <summary>空声明（未装配）：一切单元缺失（可见降级，不伪造）。</summary>
    public static readonly UiDeclaration Empty = new(
        new Dictionary<string, UiSlotSpec>(StringComparer.Ordinal),
        new Dictionary<string, UiTokenSpec>(StringComparer.Ordinal),
        new Dictionary<string, UiSkinSpec>(StringComparer.Ordinal),
        new Dictionary<string, UiPartSpec>(StringComparer.Ordinal),
        Array.Empty<string>(),
        "unavailable",
        declared: false,
        Array.Empty<string>());

    private readonly IReadOnlyDictionary<string, UiSlotSpec> _slots;
    private readonly IReadOnlyDictionary<string, UiTokenSpec> _tokens;
    private readonly IReadOnlyDictionary<string, UiSkinSpec> _skins;
    private readonly IReadOnlyDictionary<string, UiPartSpec> _parts;
    private readonly IReadOnlyList<string> _carriers;

    private UiDeclaration(
        IReadOnlyDictionary<string, UiSlotSpec> slots,
        IReadOnlyDictionary<string, UiTokenSpec> tokens,
        IReadOnlyDictionary<string, UiSkinSpec> skins,
        IReadOnlyDictionary<string, UiPartSpec> parts,
        IReadOnlyList<string> carriers,
        string fingerprint,
        bool declared,
        IReadOnlyList<string> diagnostics)
    {
        _slots = slots;
        _tokens = tokens;
        _skins = skins;
        _parts = parts;
        _carriers = carriers;
        Fingerprint = fingerprint;
        IsDeclared = declared;
        Diagnostics = diagnostics;
    }

    /// <summary>是否来自可用声明。</summary>
    public bool IsDeclared { get; }

    /// <summary>声明指纹（产物自证；未装配 = unavailable）。</summary>
    public string Fingerprint { get; }

    /// <summary>解析期可见诊断（非法条目逐条记录，禁静默）。</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>声明载体 id 集（单源 = [Table] UiCarrier；缺声明 ⇒ 空）。</summary>
    public IReadOnlyCollection<string> CarrierIds => _carriers;

    /// <summary>已声明渲染单元名集。</summary>
    public IReadOnlyCollection<string> PartNames => (IReadOnlyCollection<string>)_parts.Keys;

    /// <summary>已声明皮肤名集。</summary>
    public IReadOnlyCollection<string> SkinNames => (IReadOnlyCollection<string>)_skins.Keys;

    /// <summary>取槽位声明（未声明 ⇒ null）。</summary>
    /// <param name="name">槽位名。</param>
    public UiSlotSpec? Slot(string name)
        => name is not null && _slots.TryGetValue(name, out var slot) ? slot : null;

    /// <summary>取渲染单元声明（未声明 ⇒ null）。</summary>
    /// <param name="name">单元名。</param>
    public UiPartSpec? Part(string name)
        => name is not null && _parts.TryGetValue(name, out var part) ? part : null;

    /// <summary>按皮肤解析出一次渲染用的样式上下文（未知皮肤 ⇒ 可见诊断 + 回落默认档）。</summary>
    /// <param name="skin">皮肤 id（可空 ⇒ 默认档）。</param>
    /// <param name="overrides">宿主显式覆盖（最高优先级）。</param>
    public ResolvedUi Resolve(string? skin = null, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var diagnostics = new List<string>(Diagnostics);
        var (resolvedSkin, density, tokens) = ResolveTokens(skin, overrides, diagnostics);
        return new ResolvedUi(this, resolvedSkin, density, tokens, diagnostics);
    }

    /// <summary>解析皮肤继承链并解析 token 值（叶优先；未知 token ⇒ 诊断）。</summary>
    internal (string Skin, string Density, IReadOnlyDictionary<string, string> Tokens) ResolveTokens(
        string? requested, IReadOnlyDictionary<string, string>? overrides, List<string> diagnostics)
    {
        var chain = ResolveChain(requested, diagnostics);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in _tokens.Values) values[token.Name] = token.Value;

        // 继承链：由根到叶覆盖（叶优先）。
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            foreach (var pair in chain[i].Tokens)
            {
                if (_tokens.ContainsKey(pair.Key)) values[pair.Key] = pair.Value;
                else diagnostics.Add($"token-unknown:{pair.Key}@{chain[i].Name}");
            }
        }

        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                if (_tokens.ContainsKey(pair.Key)) values[pair.Key] = pair.Value;
                else diagnostics.Add($"token-unknown:{pair.Key}@override");
            }
        }

        var leaf = chain.Count > 0 ? chain[0] : null;
        var density = leaf?.Density ?? string.Empty;
        if (density.Length == 0)
        {
            foreach (var candidate in chain)
            {
                if (candidate.Density.Length == 0) continue;
                density = candidate.Density;
                break;
            }
        }

        return (leaf?.Name ?? string.Empty, density, values);
    }

    /// <summary>皮肤链（叶 → 根）；未知/缺省 ⇒ 回落声明缺省皮肤 + 诊断（禁静默）。</summary>
    private List<UiSkinSpec> ResolveChain(string? requested, List<string> diagnostics)
    {
        var chain = new List<UiSkinSpec>();
        var name = requested;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = DefaultSkinName();
        }
        else if (!_skins.ContainsKey(name))
        {
            diagnostics.Add($"skin-unknown:{name}");
            name = DefaultSkinName();
        }

        if (name is null) return chain;

        var guard = new HashSet<string>(StringComparer.Ordinal);
        while (name is not null && _skins.TryGetValue(name, out var skin))
        {
            if (!guard.Add(skin.Name))
            {
                diagnostics.Add($"skin-cycle:{skin.Name}");
                break;
            }

            chain.Add(skin);
            name = skin.Extends;
            if (name is not null && !_skins.ContainsKey(name))
            {
                diagnostics.Add($"skin-extends-unknown:{name}");
                break;
            }
        }

        return chain;
    }

    /// <summary>缺省皮肤：声明 <c>default = true</c> 者优先，其次首个皮肤（无皮肤 ⇒ null）。</summary>
    private string? DefaultSkinName()
    {
        foreach (var skin in _skins.Values)
        {
            if (skin.IsDefault) return skin.Name;
        }

        foreach (var name in _skins.Keys) return name;
        return null;
    }
}
