namespace Modules.Wiring.Runtime;

/// <summary>作用域定位：<c>kind:key</c>（global 无 key）。</summary>
public readonly record struct WireScopeInstance(string Scope, string Kind, string Key)
{
    public bool IsGlobal => string.Equals(Kind, "global", StringComparison.Ordinal);
}

/// <summary>
/// scope → 实例解析（page/view/task/region 等由上层提供词表；本层只做形态解析，不引领域词）。
/// <para>非法 scope 形态显式失败（不返回 default 冒充 global）。</para>
/// </summary>
public static class WireScopeRouter
{
    public const string Global = "global";

    public static bool TryResolve(string? scope, out WireScopeInstance instance)
    {
        instance = default;
        if (string.IsNullOrWhiteSpace(scope) || string.Equals(scope, Global, StringComparison.Ordinal))
        {
            instance = new WireScopeInstance(Global, Global, string.Empty);
            return true;
        }

        var separator = scope!.IndexOf(':');
        if (separator <= 0 || separator >= scope.Length - 1) return false;

        var kind = scope[..separator].Trim();
        var key = scope[(separator + 1)..].Trim();
        if (kind.Length == 0 || key.Length == 0) return false;

        instance = new WireScopeInstance(scope, kind, key);
        return true;
    }

    /// <summary>同一实例判定（global 与一切实例都相容；实例之间须全等）。</summary>
    public static bool IsSameInstance(string left, string right)
    {
        if (!TryResolve(left, out var a) || !TryResolve(right, out var b)) return false;
        if (a.IsGlobal || b.IsGlobal) return true;
        return string.Equals(a.Scope, b.Scope, StringComparison.Ordinal);
    }
}
