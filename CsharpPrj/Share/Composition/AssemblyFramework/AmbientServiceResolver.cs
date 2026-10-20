using System.Runtime.CompilerServices;

namespace Share.Composition.AssemblyFramework;

/// <summary>
/// 运行时服务解析（U4）—— 「查找 → 回退 → 留痕」的统一实现，消除各调用点重复的
/// <c>Resolve&lt;T&gt;() ?? new Default()</c> + try/catch 模板。
/// <para>本类**不拥有**任何槽承载（不引入 AsyncLocal / 静态单例），只接收调用方的查找委托；
/// 已注册实例优先，未装配或查找异常才走回退工厂。回退工厂缺失即 fail-fast（禁静默造默认实例）。</para>
/// </summary>
public static class AmbientServiceResolver
{
    /// <param name="lookup">查找委托（未装配/缺省返回 null）。</param>
    /// <param name="fallbackFactory">回退工厂；null = 无回退（未命中即抛）。</param>
    /// <param name="onFailure">查找抛异常时的留痕回调（异常 + 调用方）。</param>
    /// <param name="caller">调用方名（缺省自动取）。</param>
    public static T Resolve<T>(
        Func<T?> lookup,
        Func<T>? fallbackFactory = null,
        Action<Exception, string>? onFailure = null,
        [CallerMemberName] string? caller = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(lookup);

        try
        {
            var found = lookup();
            if (found is not null) return found;
        }
        catch (Exception ex)
        {
            onFailure?.Invoke(ex, caller ?? "unknown");
        }

        if (fallbackFactory is not null) return fallbackFactory();

        throw new InvalidOperationException(
            $"服务 {typeof(T).Name} 未装配且无回退工厂（caller={caller ?? "unknown"}）。");
    }
}
