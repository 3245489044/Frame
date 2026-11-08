#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>一条解析后的样式指令（槽位 → 载体绑定 + 已解析值）。</summary>
/// <param name="Slot">语义槽位。</param>
/// <param name="Bind">绑定方式（cssvar / prop）。</param>
/// <param name="Prop">载体属性名。</param>
/// <param name="Value">已解析 token 值（单位less）。</param>
/// <param name="Unit">载体侧声明的单位（token 不含单位）。</param>
public readonly record struct UiVisual(string Slot, string Bind, string Prop, string Value, string Unit);

/// <summary>
/// 一次渲染的样式上下文（**唯一解析产物**）：皮肤 + 密度 + token 值 + 每单元结构/样式。
/// <para>后端只消费本对象，不再自行解释皮肤或取值。</para>
/// </summary>
public sealed class ResolvedUi
{
    private readonly UiDeclaration _declaration;
    private readonly IReadOnlyList<UiAliasSpec> _aliases;
    private readonly Dictionary<string, ResolvedPart> _parts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiTheme> _themes = new(StringComparer.Ordinal);

    internal ResolvedUi(
        UiDeclaration declaration,
        string skin,
        string density,
        IReadOnlyDictionary<string, string> tokens,
        IReadOnlyList<UiAliasSpec> aliases,
        IReadOnlyList<string> diagnostics)
    {
        _declaration = declaration;
        _aliases = aliases;
        Skin = skin;
        Density = density;
        Tokens = tokens;
        Diagnostics = diagnostics;
    }

    /// <summary>生效皮肤 id（未知 ⇒ 已回落默认档）。</summary>
    public string Skin { get; }

    /// <summary>生效密度档（可空）。</summary>
    public string Density { get; }

    /// <summary>已解析 token 值（皮肤链 + 覆盖后）。</summary>
    public IReadOnlyDictionary<string, string> Tokens { get; }

    /// <summary>token → 载体变量别名（N-03：单源 = 声明面；空 = 声明缺该项）。</summary>
    public IReadOnlyList<UiAliasSpec> Aliases => _aliases;

    /// <summary>解析期可见诊断。</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>声明载体 id 集。</summary>
    public IReadOnlyCollection<string> CarrierIds => _declaration.CarrierIds;

    /// <summary>声明单元名集。</summary>
    public IReadOnlyCollection<string> PartNames => _declaration.PartNames;

    /// <summary>取已解析单元（未声明 ⇒ null：调用方须可见降级）。</summary>
    /// <param name="name">单元名。</param>
    public ResolvedPart? Part(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_parts.TryGetValue(name, out var cached)) return cached;

        var spec = _declaration.Part(name);
        if (spec is null) return null;

        var part = new ResolvedPart(spec, Tokens, _declaration.Slot);
        _parts[name] = part;
        return part;
    }

    /// <summary>按角色取单元（role 键）。</summary>
    /// <param name="role">角色（bubble/fieldRow/badge…）。</param>
    public ResolvedPart? PartForRole(string? role) => Part(role ?? string.Empty);

    /// <summary>按渲染单元取单元（cell 键，记录前缀 <c>cell_</c>）。</summary>
    /// <param name="cell">单元名（kv/tree/code…）。</param>
    public ResolvedPart? PartForCell(string? cell)
        => string.IsNullOrEmpty(cell) ? null : Part("cell_" + Normalize(cell!));

    /// <summary>按呈现类取单元（class 键，记录前缀 <c>class_</c>）。</summary>
    /// <param name="classId">呈现类 id（user-message/notice…）。</param>
    public ResolvedPart? PartForClass(string? classId)
        => string.IsNullOrEmpty(classId) ? null : Part("class_" + Normalize(classId!));

    /// <summary>
    /// 主题合并单点：结构属性取角色部件，外观取呈现类部件（类缺失/未知 ⇒ 回退角色）。
    /// <para>两者都无声明 ⇒ <c>null</c>（调用方保持零主题路径）。</para>
    /// </summary>
    /// <param name="role">渲染角色（bubble/fieldRow…）。</param>
    /// <param name="classId">呈现类 id（tool-call/answer…）。</param>
    public UiTheme? ThemeFor(string? role, string? classId)
    {
        var rolePart = PartForRole(role);
        var visualPart = PartForClass(classId) ?? rolePart;
        if (rolePart is null && visualPart is null) return null;

        var key = (role ?? string.Empty) + "\u0001" + (classId ?? string.Empty);
        if (_themes.TryGetValue(key, out var cached)) return cached;

        var theme = new UiTheme(rolePart, visualPart);
        _themes[key] = theme;
        return theme;
    }

    /// <summary>
    /// 字段单元主题：结构与外观同源（cell 部件；未声明 ⇒ <c>null</c>）。
    /// <para>字段无呈现类轴，因此不参与“角色结构 + 类外观”合并。</para>
    /// </summary>
    /// <param name="cell">单元名（kv/tree/code…）。</param>
    public UiTheme? ThemeForCell(string? cell)
    {
        var part = PartForCell(cell);
        if (part is null) return null;

        var key = "\u0002" + (cell ?? string.Empty);
        if (_themes.TryGetValue(key, out var cached)) return cached;

        var theme = new UiTheme(part, part);
        _themes[key] = theme;
        return theme;
    }

    /// <summary>声明键归一（连字符 → 下划线；记录 id 只允许标识符）。</summary>
    private static string Normalize(string value) => value.Replace('-', '_');
}

/// <summary>
/// 主题合并视图：结构来自角色部件、外观来自呈现类部件（类部件缺失/未知 ⇒ 回退角色）。
/// <para>后端只消费本对象，不自行决定“外观听谁的”。</para>
/// </summary>
public sealed class UiTheme
{
    private static readonly IReadOnlyList<UiVisual> NoVisuals = Array.Empty<UiVisual>();
    private static readonly IReadOnlyList<string> NoMissing = Array.Empty<string>();

    internal UiTheme(ResolvedPart? rolePart, ResolvedPart? visualPart)
    {
        RolePart = rolePart;
        VisualPart = visualPart;
    }

    /// <summary>结构来源部件（角色；缺失 ⇒ 结构为空）。</summary>
    public ResolvedPart? RolePart { get; }

    /// <summary>外观来源部件（呈现类；类缺失 ⇒ 角色）。</summary>
    public ResolvedPart? VisualPart { get; }

    /// <summary>实际生效的视觉部件名（<c>data-part</c> 取值）。</summary>
    public string? Name => VisualPart?.Name ?? RolePart?.Name;

    /// <summary>结构属性：只取角色部件（该载体未声明 ⇒ null）。</summary>
    /// <param name="carrierId">载体 id。</param>
    public IReadOnlyDictionary<string, string>? Structure(string carrierId)
        => RolePart?.Structure(carrierId);

    /// <summary>样式指令：取生效视觉部件（类优先，角色回退）。</summary>
    /// <param name="carrierId">载体 id。</param>
    /// <param name="state">节点状态（可空）。</param>
    public IReadOnlyList<UiVisual> Visuals(string carrierId, string? state = null)
        => VisualPart?.Visuals(carrierId, state) ?? NoVisuals;

    /// <summary>该载体真实无法承载的槽位（跟随生效视觉部件）。</summary>
    /// <param name="carrierId">载体 id。</param>
    /// <param name="state">节点状态（可空）。</param>
    public IReadOnlyList<string> MissingSlots(string carrierId, string? state = null)
        => VisualPart?.MissingSlots(carrierId, state) ?? NoMissing;
}

/// <summary>已声明单元的解析视图：结构属性（载体差）与样式指令（语义值）。</summary>
public sealed class ResolvedPart
{
    private readonly UiPartSpec _spec;
    private readonly IReadOnlyDictionary<string, string> _tokens;
    private readonly Func<string, UiSlotSpec?> _slotOf;

    internal ResolvedPart(UiPartSpec spec, IReadOnlyDictionary<string, string> tokens, Func<string, UiSlotSpec?> slotOf)
    {
        _spec = spec;
        _tokens = tokens;
        _slotOf = slotOf;
    }

    /// <summary>单元名。</summary>
    public string Name => _spec.Name;

    /// <summary>单元类别（role / cell / class）。</summary>
    public string Kind => _spec.Kind;

    /// <summary>结构属性（element/control/cls/prefix…）；该载体未声明 ⇒ null（可见降级）。</summary>
    /// <param name="carrierId">载体 id。</param>
    public IReadOnlyDictionary<string, string>? Structure(string carrierId)
        => _spec.Carriers.TryGetValue(carrierId, out var props) ? props : null;

    /// <summary>样式指令（槽位 → 绑定 + 值）；<paramref name="state"/> 命中时按状态覆盖槽位。</summary>
    /// <param name="carrierId">载体 id。</param>
    /// <param name="state">节点状态（open/closed/disabled/unknown…；可空）。</param>
    public IReadOnlyList<UiVisual> Visuals(string carrierId, string? state = null)
    {
        var visuals = new List<UiVisual>();
        foreach (var pair in EffectiveSlots(state))
        {
            if (!_tokens.TryGetValue(pair.Value, out var value)) continue;
            var slot = _slotOf(pair.Key);
            if (slot is null || !slot.Bindings.TryGetValue(carrierId, out var binding)) continue;
            if (string.Equals(binding.Bind, "none", StringComparison.Ordinal)) continue;
            if (binding.Prop.Length == 0) continue;
            visuals.Add(new UiVisual(pair.Key, binding.Bind, binding.Prop, value, binding.Unit));
        }

        return visuals;
    }

    /// <summary>该载体**真实**无法承载的槽位（不可承载清单）。</summary>
    /// <remarks>
    /// <c>bind = none</c> 或该载体未声明绑定 = 该载体有意不支持（不计告警，否则文本载体逐槽位刷噪声）；
    /// 只有「token 值缺失」与「声明了绑定却缺属性名」才是真实缺口。
    /// </remarks>
    /// <param name="carrierId">载体 id。</param>
    /// <param name="state">节点状态（可空）。</param>
    public IReadOnlyList<string> MissingSlots(string carrierId, string? state = null)
    {
        var missing = new List<string>();
        foreach (var pair in EffectiveSlots(state))
        {
            if (!_tokens.ContainsKey(pair.Value))
            {
                missing.Add(pair.Key);
                continue;
            }

            var slot = _slotOf(pair.Key);
            if (slot is null)
            {
                missing.Add(pair.Key);
                continue;
            }

            if (!slot.Bindings.TryGetValue(carrierId, out var binding)) continue;
            if (string.Equals(binding.Bind, "none", StringComparison.Ordinal)) continue;
            if (binding.Prop.Length == 0) missing.Add(pair.Key);
        }

        return missing;
    }

    /// <summary>生效槽位表：基础槽位 + 状态覆盖（<c>any</c> 先、具体状态后）。</summary>
    private IReadOnlyDictionary<string, string> EffectiveSlots(string? state)
    {
        if (_spec.States.Count == 0 && string.IsNullOrEmpty(state)) return _spec.Slots;

        var map = new Dictionary<string, string>(_spec.Slots, StringComparer.Ordinal);
        if (_spec.States.TryGetValue("any", out var any)) Merge(map, any);
        if (!string.IsNullOrEmpty(state) && _spec.States.TryGetValue(state, out var exact)) Merge(map, exact);
        return map;
    }

    private static void Merge(Dictionary<string, string> target, IReadOnlyDictionary<string, string> overrides)
    {
        foreach (var pair in overrides) target[pair.Key] = pair.Value;
    }
}
