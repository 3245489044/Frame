#nullable enable

using System;
using System.Collections.Generic;

namespace UiBackend.Abstractions;

/// <summary>
/// 视图词表（通用机制，**不含具体视图名**）：登记「视图键 ↔ 作用域键」双向唯一映射。
/// <para>存在理由：视图 → 受控词的映射散落在引擎/投影/悬浮层多处，任一处漏改即「按另一视图取形」；
/// 收敛为一张注册表后，未知词默认拒绝（default-deny），不再静默回退到某一视图。</para>
/// </summary>
public sealed class UiViewVocabulary
{
    private readonly Dictionary<string, string> _scopeByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _keyByScope = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>登记一条映射（幂等同值；冲突 ⇒ 抛：一作用域只能对应一视图键）。</summary>
    public UiViewVocabulary Register(string viewKey, string scopeKey)
    {
        if (string.IsNullOrWhiteSpace(viewKey) || string.IsNullOrWhiteSpace(scopeKey))
            throw new ArgumentException("视图键与作用域键均不可为空。");

        var key = viewKey.Trim();
        var scope = scopeKey.Trim();
        if (_scopeByKey.TryGetValue(key, out var existingScope)
            && !string.Equals(existingScope, scope, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"视图键映射冲突: {key} → {existingScope} / {scope}");
        }

        if (_keyByScope.TryGetValue(scope, out var existingKey)
            && !string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"作用域键映射冲突: {scope} → {existingKey} / {key}");
        }

        _scopeByKey[key] = scope;
        _keyByScope[scope] = key;
        return this;
    }

    /// <summary>视图键 → 作用域键（未登记 ⇒ 抛）。</summary>
    public string RequireScope(string viewKey)
        => _scopeByKey.TryGetValue((viewKey ?? string.Empty).Trim(), out var scope)
            ? scope
            : throw new InvalidOperationException($"视图键未登记: {viewKey}（声明面缺失）");

    /// <summary>作用域键 → 视图键（未登记 ⇒ 抛）。</summary>
    public string RequireKey(string scopeKey)
        => _keyByScope.TryGetValue((scopeKey ?? string.Empty).Trim(), out var key)
            ? key
            : throw new InvalidOperationException($"作用域键未登记: {scopeKey}（声明面缺失）");

    /// <summary>视图键 → 作用域键（未登记 ⇒ false）。</summary>
    public bool TryScope(string? viewKey, out string scope)
        => _scopeByKey.TryGetValue((viewKey ?? string.Empty).Trim(), out scope!);

    /// <summary>作用域键 → 视图键（未登记 ⇒ false）。</summary>
    public bool TryKey(string? scopeKey, out string key)
        => _keyByScope.TryGetValue((scopeKey ?? string.Empty).Trim(), out key!);
}
