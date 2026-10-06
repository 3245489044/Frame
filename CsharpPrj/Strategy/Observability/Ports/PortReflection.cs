using System.Reflection;

namespace Strategy.Observability.Ports;

/// <summary>
/// 结构事实查询（内核公共小件，领域无关）：「结构契约是否成立」（端口实现、方法存在、返回类型形状、
/// 程序集引用方向）的取值机制单源。断言内容（期望值 / 领域谓词）仍留应用侧与宿主侧。
/// <para>迁移说明：逐字承接测试侧 L0 同机制件（<c>TestInfrastructure.DataDriven.SpecReflection</c>，
/// 该件随本件落地退场，消两套重复实现）；差异仅 <see cref="ReturnsTaskOf"/> 由泛型改收目标类型实参
/// （内核不引具体领域类型）。</para>
/// </summary>
public static class PortReflection
{
    /// <summary>类型是否实现指定接口（按接口简单名匹配，<c>GetInterface</c> 语义）。</summary>
    public static bool Implements(Type type, string interfaceName)
    {
        PortGuard.Required(type, nameof(type));
        return type.GetInterface(interfaceName) is not null;
    }

    /// <summary>类型是否公开声明指定名方法（<c>GetMethod</c> 默认绑定标志语义）。</summary>
    public static bool HasMethod(Type type, string methodName)
    {
        PortGuard.Required(type, nameof(type));
        return type.GetMethod(methodName) is not null;
    }

    /// <summary>指定方法的返回类型是否为 <c>Task&lt;T&gt;</c>（<paramref name="resultType"/> = T；方法不存在 → false）。</summary>
    public static bool ReturnsTaskOf(Type type, string methodName, Type resultType)
    {
        PortGuard.Required(type, nameof(type));
        PortGuard.Required(resultType, nameof(resultType));
        return type.GetMethod(methodName)?.ReturnType == typeof(Task<>).MakeGenericType(resultType);
    }

    /// <summary>类型所在程序集直接引用的程序集名清单（不含 null 名；含全部引用，调用方自行过滤）。</summary>
    public static IReadOnlyList<string> ReferencedAssemblyNames(Type type)
    {
        PortGuard.Required(type, nameof(type));
        return type.Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToArray();
    }

    /// <summary>
    /// 程序集类型枚举（加载器异常带定位上抛）——声明扫描（实现标注 <see cref="PortImplementationRegistry"/> /
    /// 探针路由 <see cref="PortProbeRouter"/>）共用的唯一入口，避免两套健壮性口径。
    /// </summary>
    public static IReadOnlyList<Type> TypesOf(Assembly assembly)
    {
        PortGuard.Required(assembly, nameof(assembly));

        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            throw new InvalidOperationException(
                $"[port:scan] 程序集 '{assembly.GetName().Name}' 类型枚举失败（加载器异常：" +
                string.Join("; ", ex.LoaderExceptions.Select(e => e?.Message ?? e?.GetType().Name)) + "）", ex);
        }
    }

    /// <summary>
    /// 调用**非公开静态**方法（按「名 + 参数个数」解析，参数个数不匹配时取参数最少者）：
    /// 方法缺失 / 返回类型不符 → fail-fast（含类型名、方法名、期望与实际）。
    /// <para>
    /// 用途：产品未公开入口的结构探针单源（取代逐处手写 <c>GetMethod</c> + <c>BindingFlags</c> + 强转）。
    /// 边界声明：只**单源化机制**，不解决私有可见性根因。
    /// </para>
    /// </summary>
    public static T InvokeNonPublicStatic<T>(Type type, string methodName, params object?[] args)
    {
        PortGuard.Required(type, nameof(type));
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        args ??= Array.Empty<object?>();

        var candidates = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
            throw new InvalidOperationException(
                $"[port:bind] 类型 {type.Name} 上找不到静态方法 {methodName}（含非公开）");

        var method = candidates.FirstOrDefault(m => m.GetParameters().Length == args.Length)
                     ?? candidates.OrderBy(m => m.GetParameters().Length).First();

        if (!typeof(T).IsAssignableFrom(method.ReturnType))
            throw new InvalidOperationException(
                $"[port:bind] 方法 {type.Name}.{methodName} 的返回类型 {method.ReturnType.Name} 与期望 {typeof(T).Name} 不符");

        try
        {
            return (T)method.Invoke(null, args)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner)
        {
            return (T)PortInvocation.ExpectAny(inner, expect: null)!;   // 原样上抛（保留原始栈）
        }
    }
}
