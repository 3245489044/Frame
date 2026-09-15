using System.Reflection;

namespace Strategy.Observability.Ports;

/// <summary>
/// 端口实现声明（E16）：把「键 → 实现」从手写登记表降为方法标注。
/// 键为不透明字符串（机制件不解析其内部结构，仅 Ordinal 比较）。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PortImplementationAttribute(string portKey) : Attribute
{
    /// <summary>端口键（= 标准面 impl.id；机械同值，由守卫对账）。</summary>
    public string PortKey { get; } = string.IsNullOrWhiteSpace(portKey)
        ? throw new ArgumentException("端口键不能为空", nameof(portKey))
        : portKey;
}

/// <summary>
/// 端口实现收集（E16）：从指定程序集扫描 <see cref="PortImplementationAttribute"/> 方法 →
/// 「键 → 实现委托」。反射只作用于**调用方传入的程序集**，机制件不知领域位置。
/// <para>
/// 纪律：① 方法须为静态 ∧ 恰一个 <see cref="IPortInput"/> 形参，违规即抛（签名漂移不静默）；
/// ② 同键重复即抛（单一实现点）；③ 结果按键 Ordinal 升序（跨次确定性）。
/// </para>
/// </summary>
public static class PortImplementationRegistry
{
    /// <summary>收集程序集内全部标注方法（键 Ordinal 升序；空标注 = 空字典，不是错误）。</summary>
    public static IReadOnlyDictionary<string, Func<IPortInput, object?>> Collect(Assembly assembly)
    {
        var collected = new SortedDictionary<string, Func<IPortInput, object?>>(StringComparer.Ordinal);

        RegisterInto(assembly, (key, invoke) =>
        {
            if (!collected.TryAdd(key, invoke))
                throw new InvalidOperationException($"[port:impl] 端口键 '{key}' 重复标注（单一实现点被破坏）");
        });

        return collected;
    }

    /// <summary>
    /// 逐条交给调用方登记口（复用其重复即抛语义，如端口目录的单一登记点）；
    /// 返回标注方法数。键缺失/重复由调用方登记口裁决。
    /// </summary>
    public static int RegisterInto(Assembly assembly, Action<string, Func<IPortInput, object?>> register)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(register);

        var count = 0;
        foreach (var method in AnnotatedMethods(assembly))
        {
            var key = method.GetCustomAttribute<PortImplementationAttribute>()!.PortKey;
            register(key, Bind(key, method));
            count++;
        }

        return count;
    }

    /// <summary>全程序集标注方法（含非公开类型 / 非公开方法；声明序稳定 = 类型名 + 方法名 Ordinal）。</summary>
    private static IEnumerable<MethodInfo> AnnotatedMethods(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            throw new InvalidOperationException(
                $"[port:impl] 程序集 '{assembly.GetName().Name}' 类型枚举失败（加载器异常：" +
                string.Join("; ", ex.LoaderExceptions.Select(e => e?.Message ?? e?.GetType().Name)) +
                "）", ex);
        }

        return types
            .OrderBy(t => t.FullName ?? t.Name, StringComparer.Ordinal)
            .SelectMany(t => t
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<PortImplementationAttribute>() is not null)
                .OrderBy(m => m.Name, StringComparer.Ordinal));
    }

    /// <summary>标注方法 → 实现委托（签名校验前置；绑定失败即抛，含键与方法名定位）。</summary>
    private static Func<IPortInput, object?> Bind(string key, MethodInfo method)
    {
        if (!method.IsStatic || method.GetParameters() is not [{ } parameter] || !parameter.ParameterType.IsAssignableFrom(typeof(IPortInput)))
            throw new InvalidOperationException(
                $"[port:impl] 端口 '{key}' 的标注方法 '{method.DeclaringType?.FullName}.{method.Name}' 签名非法" +
                "（要求：静态 ∧ 恰一个 IPortInput 形参）");

        try
        {
            return method.CreateDelegate<Func<IPortInput, object?>>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[port:impl] 端口 '{key}' 的标注方法 '{method.DeclaringType?.FullName}.{method.Name}' 绑定失败：{ex.Message}", ex);
        }
    }
}
