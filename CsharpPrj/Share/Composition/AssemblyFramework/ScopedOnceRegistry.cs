using System;
using System.Collections.Concurrent;

namespace Share.Composition.AssemblyFramework;

/// <summary>
/// 注册键身份（scope + kind + name）：同一 name 在不同面/不同注册者下互不吞并。
/// </summary>
public readonly record struct RegistrationKey(string Scope, string Kind, string Name)
{
    /// <summary>构造并归一化（三段各自 trim；空段拒绝）。</summary>
    public static RegistrationKey Of(string scope, string kind, string name)
    {
        if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("注册面不能为空。", nameof(scope));
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("注册类别不能为空。", nameof(kind));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("注册名不能为空。", nameof(name));
        return new RegistrationKey(scope.Trim(), kind.Trim(), name.Trim());
    }

    /// <summary>兼容构造：只给 name ⇒ kind = <c>default</c>。</summary>
    public static RegistrationKey Of(string scope, string name) => Of(scope, "default", name);

    /// <summary>结构化口径（诊断/日志；与内部键一致）。</summary>
    public override string ToString() => Scope + "\u001F" + Kind + "\u001F" + Name;
}

/// <summary>
/// 分面一次注册表（通用件）：键身份 = (scope, kind, name)；冻结按面判定且只拒**新键**。
/// <para>同一 name 换 kind（如「注册者标识」与「函数名」）互不吞并 —— 这是幂等守卫键语义单源。</para>
/// </summary>
public sealed class ScopedOnceRegistry
{
    private readonly ConcurrentDictionary<string, byte> _entered = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _frozen = new(StringComparer.Ordinal);

    /// <summary>登记首次进入；该键首次出现返回 true，重复返回 false。</summary>
    /// <exception cref="InvalidOperationException">该面已冻结且为新键。</exception>
    public bool TryEnter(RegistrationKey key)
    {
        var id = key.ToString();
        if (_entered.ContainsKey(id)) return false;
        if (_frozen.ContainsKey(key.Scope))
        {
            throw new InvalidOperationException(
                $"decl.orphan-impl: 注册面 {key.Scope} 已冻结，拒绝晚注册「{key.Kind}:{key.Name}」。");
        }

        return _entered.TryAdd(id, 0);
    }

    /// <summary>冻结注册面（幂等）：冻结后该面不接受新登记。</summary>
    public void Freeze(string scope)
    {
        if (!string.IsNullOrWhiteSpace(scope)) _frozen.TryAdd(scope.Trim(), 0);
    }

    /// <summary>该面是否已冻结。</summary>
    public bool IsFrozen(string scope) => !string.IsNullOrWhiteSpace(scope) && _frozen.ContainsKey(scope.Trim());

    /// <summary>是否已登记（诊断/测试用；不改变幂等状态）。</summary>
    public bool IsEntered(RegistrationKey key) => _entered.ContainsKey(key.ToString());

    /// <summary>已登记键数（诊断）。</summary>
    public int Count => _entered.Count;
}
