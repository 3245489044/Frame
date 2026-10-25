#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace UiBackend.Abstractions;

/// <summary>
/// 选择槽的键类型（领域注册的受控词）—— 框架不解释语义, 只做**写入一致性**校验:
/// 同一 (作用域, 槽) 不得被两种键类型混写(治「历史 id 与任务键共用一个槽」的跨键域污染)。
/// </summary>
public readonly record struct UiKeyKind(string Name)
{
    /// <summary>未声明键类型（跳过一致性校验; 迁移期兼容）。</summary>
    public static readonly UiKeyKind None = new(string.Empty);

    /// <summary>是否未声明键类型。</summary>
    public bool IsNone => string.IsNullOrEmpty(Name);

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>选择槽的恢复策略（缺失时的回退口径; 禁静默默认）。</summary>
public enum UiSelectionRestore
{
    /// <summary>不恢复（只认本次回投）。</summary>
    None = 0,

    /// <summary>粘滞: 恢复该 (作用域, 槽) 上次选中值。</summary>
    Sticky = 1,

    /// <summary>由领域提供的声明默认（如「当前历史首个任务」）兜底。</summary>
    DeclaredDefault = 2,
}

/// <summary>
/// 选择槽声明（作用域 × 槽 → 键类型 + 恢复策略）——
/// 「谁必须被恢复」「这个槽装的是哪一类键」由声明面单源给出, 不再靠调用点命名约定。
/// </summary>
public readonly record struct UiSelectionSlotDecl(
    string Scope,
    string Slot,
    UiKeyKind KeyKind,
    UiSelectionRestore Restore);

/// <summary>锚的三态（对齐 VisibleValue: 已知 / 未知 / 不适用; 禁以占位键冒充已知）。</summary>
public enum UiAnchorState
{
    /// <summary>已解析到具体键。</summary>
    Known = 0,

    /// <summary>声明面适用但当前没有选中（渲染面须显式占位）。</summary>
    Unknown = 1,

    /// <summary>该作用域不适用该锚（不渲染）。</summary>
    NotApplicable = 2,
}

/// <summary>解析后的锚（<see cref="Key"/> 仅 <see cref="UiAnchorState.Known"/> 时有意义）。</summary>
public readonly record struct UiAnchor(string? Key, UiAnchorState State)
{
    /// <summary>构造已解析锚。</summary>
    public static UiAnchor Known(string key) => new(key, UiAnchorState.Known);

    /// <summary>未知锚（须由渲染面显式可见化）。</summary>
    public static UiAnchor Unknown { get; } = new(null, UiAnchorState.Unknown);

    /// <summary>不适用锚（不渲染）。</summary>
    public static UiAnchor NotApplicable { get; } = new(null, UiAnchorState.NotApplicable);

    /// <summary>是否持有可用键。</summary>
    public bool HasKey => State == UiAnchorState.Known && !string.IsNullOrWhiteSpace(Key);
}

/// <summary>
/// 选择态存储（通用件, 态外置）—— 把「选中了什么」建模为 <c>作用域 × 槽 → 键</c>, 而不是扁平单槽。
/// <para>
/// 存在理由: 扁平单槽无法表达「同一页面多个作用域各自独立选中」, 领域只能靠命名拼接自造分槽,
/// 且不同键域(任务键 / 历史键)会落到同一槽 ⇒ 切作用域即丢选中、甚至跨键误用。
/// 本类以 <see cref="UiSelectionSlotDecl"/> 声明键类型与恢复策略, <see cref="Set"/> 做跨键域拦截。
/// </para>
/// <para>线程安全: 值表 ConcurrentDictionary; 活动作用域经锁保护（后写胜出）。</para>
/// </summary>
public sealed class UiSelectionStore
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, UiSelectionSlotDecl> _decls = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private string? _activeScope;

    /// <summary>当前活动作用域（页面级; 空 ⇒ 由调用方按视图派生）。</summary>
    public string? ActiveScope
    {
        get { lock (_gate) return _activeScope; }
        set
        {
            lock (_gate)
            {
                _activeScope = string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
            }
        }
    }

    /// <summary>登记槽声明（幂等覆盖; 同名 (作用域, 槽) 以最后一次声明为准）。</summary>
    public void Declare(IEnumerable<UiSelectionSlotDecl> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        foreach (var decl in declarations)
        {
            if (string.IsNullOrWhiteSpace(decl.Scope) || string.IsNullOrWhiteSpace(decl.Slot)) continue;
            _decls[KeyOf(decl.Scope, decl.Slot)] = decl;
        }
    }

    /// <summary>读取槽值（未设置 / 已清空 ⇒ null）。</summary>
    public string? Get(string? scope, string slot)
    {
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(slot)) return null;
        return _values.TryGetValue(KeyOf(scope!, slot), out var value) ? value : null;
    }

    /// <summary>读取活动作用域下的槽值。</summary>
    public string? GetActive(string slot)
    {
        lock (_gate) return Get(_activeScope, slot);
    }

    /// <summary>
    /// 写入槽值（空白 ⇒ 清空）。<paramref name="keyKind"/> 非空且与声明不一致 ⇒ 抛
    /// <see cref="InvalidOperationException"/>（状态不一致: 跨键域混写属结构缺陷, 不做静默纠正）。
    /// </summary>
    public void Set(string scope, string slot, string? value, UiKeyKind? keyKind = null)
    {
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(slot))
            throw new ArgumentException("选择槽必须声明作用域与槽名。", nameof(scope));

        var key = KeyOf(scope, slot);
        if (string.IsNullOrWhiteSpace(value))
        {
            _values.TryRemove(key, out _);
            return;
        }

        if (keyKind is { IsNone: false } kind
            && _decls.TryGetValue(key, out var decl)
            && !decl.KeyKind.IsNone
            && !string.Equals(decl.KeyKind.Name, kind.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"选择槽键类型不一致: scope={scope}, slot={slot}, 声明={decl.KeyKind.Name}, 写入={kind.Name}。" +
                "同一作用域槽不得承载两种键域（历史键与任务键须分槽）。");
        }

        _values[key] = value!.Trim();
    }

    /// <summary>清空槽值。</summary>
    public void Clear(string? scope, string slot)
    {
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(slot)) return;
        _values.TryRemove(KeyOf(scope!, slot), out _);
    }

    /// <summary>取某作用域的全部槽快照（诊断/回投对账用）。</summary>
    public IReadOnlyDictionary<string, string?> Snapshot(string? scope)
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(scope)) return snapshot;
        var prefix = scope!.Trim() + "/";
        foreach (var kv in _values)
        {
            if (kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                snapshot[kv.Key.Substring(prefix.Length)] = kv.Value;
        }

        foreach (var kv in _decls)
        {
            if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var slot = kv.Key.Substring(prefix.Length);
            if (!snapshot.ContainsKey(slot)) snapshot[slot] = null;
        }

        return snapshot;
    }

    /// <summary>读取槽声明的恢复策略（未声明 ⇒ <see cref="UiSelectionRestore.None"/>）——消费方据此走统一回退链，
    /// 不再各自硬编码恢复口径（声明面即契约）。</summary>
    public UiSelectionRestore RestoreOf(string? scope, string slot)
    {
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(slot)) return UiSelectionRestore.None;
        return _decls.TryGetValue(KeyOf(scope, slot), out var decl) ? decl.Restore : UiSelectionRestore.None;
    }

    private static string KeyOf(string? scope, string slot) => scope + "/" + slot;
}

/// <summary>
/// 锚解析（通用件, 单一实现）—— 回退链只在此处维护:
/// 显式回投 → 粘滞（本作用域上次选中）→ 声明默认（领域提供）→ <see cref="UiAnchor.Unknown"/>。
/// <para>
/// 反模式对照: 各命令各自写「空 ⇒ 取默认值」是 <c>ANTI-SEMANTIC-EXEMPTION</c> 的温床
/// （默认值静默冒充选中, 违反 NoSilentOutlet）; 切换作用域与刷新必须走同一条回退链。
/// </para>
/// </summary>
public static class UiSelectionBinder
{
    /// <summary>
    /// 解析单个锚。<paramref name="incoming"/> = 本次回投的显式值（最高优先）。
    /// </summary>
    public static UiAnchor Resolve(
        UiSelectionStore store,
        string scope,
        string slot,
        string? incoming,
        UiSelectionRestore restore = UiSelectionRestore.Sticky,
        Func<string?>? declaredDefault = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (!string.IsNullOrWhiteSpace(incoming)) return UiAnchor.Known(incoming!.Trim());

        if (restore is UiSelectionRestore.Sticky or UiSelectionRestore.DeclaredDefault)
        {
            var stored = store.Get(scope, slot);
            if (!string.IsNullOrWhiteSpace(stored)) return UiAnchor.Known(stored!);
        }

        if (restore == UiSelectionRestore.DeclaredDefault && declaredDefault is not null)
        {
            var fallback = declaredDefault();
            if (!string.IsNullOrWhiteSpace(fallback)) return UiAnchor.Known(fallback!);
        }

        return UiAnchor.Unknown;
    }
}
