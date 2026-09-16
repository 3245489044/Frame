using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>
/// 反射端口绑定（E15 / G2 · 原测试侧 <c>Bridge\ReflectivePortBinding.cs</c> 上移）：按声明
/// （<see cref="PortBindingSpec.Target"/>/<see cref="PortBindingSpec.Method"/>/<see cref="PortBindingSpec.Args"/>）解析目标方法、
/// 依声明转换位置实参并调用，返回值即 subject（调用记录与端口族事件由 <see cref="PortCatalog"/> 统一出证）。
/// <para>
/// 参数来源：位置实参 = 调用方直传文本（按 <c>convert</c> 转换）；结构化输入 = 具名实参 <c>with</c>
/// （原始 JSON，按目标形参类型反序列化）。目标方法抛出的业务异常原样上抛（调用记录照记 errorClass）。
/// </para>
/// <para>迁移说明：逐字搬语义；差异仅诊断异常类型由测试侧 <c>SpecRunException</c> 换为
/// <see cref="PortBindingViolationException"/>（内核不可引测试件），消息体保持逐字可定位。</para>
/// </summary>
public sealed class ReflectiveBinding : IPortBinding
{
    private readonly PortBindingSpec _spec;
    private readonly MethodInfo _method;

    /// <summary>按声明构造绑定：缺 <c>target/method</c> 即抛；目标类型/方法不可解析即抛（fail-fast，防静默绑错）。</summary>
    public ReflectiveBinding(PortBindingSpec spec)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.Target) || string.IsNullOrWhiteSpace(spec.Method))
            throw new PortBindingViolationException(
                spec.PortKey, $"端口 '{spec.PortKey}' 声明 reflective 绑定但缺少 target/method");

        _method = ReflectivePortResolver.ResolveMethod(spec, ReflectivePortResolver.ResolveType(spec.Target));
    }

    public ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default)
        => ValueTask.FromResult(Invoke(input));

    private object? Invoke(IPortInput input)
    {
        try
        {
            return _method.Invoke(null, BuildArguments(input));
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();   // 业务异常原样上抛（错误分类交调用记录）
            throw;                                          // 不可达（Throw 不返回）
        }
    }

    /// <summary>按声明转换位置实参；未声明槽位（如尾部注入参数）取该类型默认值。</summary>
    private object?[] BuildArguments(IPortInput input)
    {
        var parameters = _method.GetParameters();
        var args = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
            args[i] = i < _spec.Args.Count
                ? Convert(parameters[i], _spec.Args[i], input, i)
                : ReflectivePortResolver.DefaultFor(parameters[i].ParameterType);

        return args;
    }

    private object? Convert(ParameterInfo parameter, PortArgSpec spec, IPortInput input, int index)
    {
        if (string.Equals(spec.Convert, "json", StringComparison.Ordinal))
            return FromWith(parameter, spec, input);

        var present = index < input.Args.Count && input.Args[index] is not null;
        if (!present)
        {
            if (!spec.Optional)
                throw new PortBindingViolationException(
                    _spec.PortKey,
                    $"{_spec.PortKey} 需要第 {index} 个位置实参（声明 {DeclaredText()}），实际只有 {input.Args.Count} 个");

            return spec.Default is { } fallback
                ? ReflectivePortResolver.FromJson(fallback, parameter.ParameterType)
                : ReflectivePortResolver.DefaultFor(parameter.ParameterType);
        }

        var raw = input.Args[index];
        if (spec.NullLiteral && raw is string literal && string.Equals(literal, "null", StringComparison.Ordinal)) return null;

        if (PortValues.IsDirectPositional(spec.Convert, raw))
            return PortValues.FitsDirect(raw, parameter.ParameterType)
                ? raw
                : throw new PortBindingViolationException(
                    _spec.PortKey,
                    $"{_spec.PortKey} 第 {index} 个位置实参按对象直投，但与形参 {parameter.ParameterType.Name} 不兼容");

        var text = raw as string ?? raw?.ToString() ?? string.Empty;

        return spec.Convert switch
        {
            "string" => text,
            "csv" => SplitCsv(text).ToList(),
            "bool" => ParseBool(text, index),
            "int" => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue)
                ? intValue
                : throw Mismatch(text, index, "整数"),
            "double" => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)
                ? doubleValue
                : throw Mismatch(text, index, "数值"),
            "json" => FromWith(parameter, spec, input),
            _ => throw Mismatch(spec.Convert, index, "string|bool|int|double|csv|json|object"),
        };
    }

    /// <summary>结构化直传：取具名实参 <c>with</c> 的原始 JSON 并按目标形参类型反序列化。</summary>
    private object? FromWith(ParameterInfo parameter, PortArgSpec spec, IPortInput input)
    {
        if (string.IsNullOrWhiteSpace(spec.Key))
            throw new PortBindingViolationException(
                _spec.PortKey, $"端口 '{_spec.PortKey}' 的 json 实参缺少 key");

        if (!input.With.TryGetValue(spec.Key!, out var value) || value is null)
        {
            if (!spec.Optional)
                throw new PortBindingViolationException(
                    _spec.PortKey, $"端口 '{_spec.PortKey}' 缺具名实参 '{spec.Key}'（with）");

            // 缺参口径与位置实参分支同源：声明 default → 按目标形参类型还原；否则取目标类型默认值。
            return spec.Default is { } fallback
                ? ReflectivePortResolver.FromJson(fallback, parameter.ParameterType)
                : ReflectivePortResolver.DefaultFor(parameter.ParameterType);
        }

        return ReflectivePortResolver.FromJson(value, parameter.ParameterType);
    }

    private bool ParseBool(string text, int index)
        => bool.TryParse(text, out var value)
            ? value
            : text is "1" or "0"
                ? text == "1"
                : throw Mismatch(text, index, "布尔");

    private PortBindingViolationException Mismatch(string text, int index, string expected)
        => new(_spec.PortKey, $"端口 '{_spec.PortKey}' 第 {index} 个实参 '{text}' 不是{expected}");

    /// <summary>声明文本（诊断用；位序即语义）。</summary>
    private string DeclaredText() => string.Join(",", _spec.Args.Select(a => a.Convert));

    /// <summary>逗号分隔 → 去空段去空白数组（口径与 DataDriven 侧 <c>SpecLiterals.SplitCsv</c> 一致）。</summary>
    private static string[] SplitCsv(string text)
        => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>反射端口绑定的解析小件（类型 / 方法 / 参数默认值 / JSON 反序列化）：分离后便于守卫与单测直接锁定。</summary>
internal static class ReflectivePortResolver
{
    private static readonly Dictionary<string, Type> TypeCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, MethodInfo> MethodCache = new(StringComparer.Ordinal);

    /// <summary>解析目标类型：已加载程序集 → 输出目录探测（宿主按二进制引用装配，目标程序集可能尚未加载）→ <c>Type.GetType</c>。</summary>
    internal static Type ResolveType(string fullName)
    {
        if (TypeCache.TryGetValue(fullName, out var cached)) return cached;

        var type = PortTypeLoader.Find(fullName)
            ?? throw new InvalidOperationException(
                $"[port:bind] 目标类型 '{fullName}' 未加载（检查工程二进制引用与命名空间）");

        TypeCache[fullName] = type;
        return type;
    }

    /// <summary>
    /// 解析目标方法：优先「公开 + 参数个数 = 声明个数」的候选；无精确匹配则取参数最少者（尾部参数按默认值补齐）。
    /// 同名方法零候选即抛（防静默绑错）。
    /// </summary>
    internal static MethodInfo ResolveMethod(PortBindingSpec spec, Type type)
        => ResolveMethod(type, spec.PortKey, spec.Method, spec.Args.Count, isStatic: true);

    /// <summary>方法解析（声明式组合绑定用：同一口径支持静态 / 实例两种绑定标志）。</summary>
    internal static MethodInfo ResolveMethod(Type type, string portKey, string methodName, int declaredCount, bool isStatic)
    {
        var cacheKey = $"{type.FullName}.{methodName}/{declaredCount}/{(isStatic ? "s" : "i")}";
        if (MethodCache.TryGetValue(cacheKey, out var cached)) return cached;

        var flags = BindingFlags.Public | BindingFlags.FlattenHierarchy
                    | (isStatic ? BindingFlags.Static : BindingFlags.Instance);

        var candidates = type
            .GetMethods(flags)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
            throw new InvalidOperationException(
                $"[port:bind] 端口 '{portKey}'：类型 {type.Name} 上找不到方法 {methodName}");

        var method = candidates.FirstOrDefault(m => DeclaredCount(m) == declaredCount)
                     ?? candidates.FirstOrDefault(m => m.GetParameters().Length == declaredCount)
                     ?? candidates.OrderBy(m => m.GetParameters().Length).First();

        MethodCache[cacheKey] = method;
        return method;
    }

    /// <summary>
    /// 声明实参数对应的方法形参数：<c>out</c> 形参不计（<c>invoke.out</c> 由出参捕获按位写入槽，
    /// 声明面不为出参写实参）——见《…声明式编排框架》§9.1 试点写法。
    /// </summary>
    private static int DeclaredCount(MethodInfo method)
        => method.GetParameters().Count(parameter => !parameter.IsOut);

    /// <summary>成员读取解析（属性优先、缺失回落静态 / 实例字段）：属性与字段是产品表示选择，声明侧不区分。</summary>
    internal static MemberInfo ResolveMember(Type type, string portKey, string name, bool isStatic)
    {
        var flags = BindingFlags.Public | BindingFlags.FlattenHierarchy
                    | (isStatic ? BindingFlags.Static : BindingFlags.Instance);

        return (MemberInfo?)type.GetProperty(name, flags)
               ?? type.GetField(name, flags)
               ?? throw new InvalidOperationException(
                   $"[port:bind] 端口 '{portKey}'：类型 {type.Name} 上找不到成员 {name}");
    }

    /// <summary>目标形参的默认值（值类型 → 默认实例；<c>CancellationToken</c> → None；引用类型 → null）。</summary>
    internal static object? DefaultFor(Type type)
        => type == typeof(CancellationToken)
            ? CancellationToken.None
            : type.IsValueType ? Activator.CreateInstance(type) : null;

    /// <summary>JSON 值 → 目标类型（JsonElement 按原始文本反序列化；其余直传）。</summary>
    internal static object? FromJson(object value, Type targetType)
    {
        if (value is not JsonElement element) return value;
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;

        return targetType == typeof(JsonElement)
            ? element.Clone()
            : JsonSerializer.Deserialize(element.GetRawText(), targetType);
    }
}

/// <summary>
/// 目标类型加载（反射绑定的类型可达性）：已加载程序集 → 输出目录探测 → <c>Type.GetType</c>。
/// <para>
/// 宿主按**二进制引用**装配（不 ProjectReference 目标源码）时目标程序集可能尚未进入 AppDomain；
/// 输出目录（<see cref="AppContext.BaseDirectory"/>）即二进制闭包落点，按名探测即可完成可达化。
/// </para>
/// </summary>
internal static class PortTypeLoader
{
    private static readonly Lazy<IReadOnlyList<Assembly>> Probed = new(LoadProbed);

    /// <summary>按全名查找类型；未找到 → null。</summary>
    internal static Type? Find(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (Get(assembly, fullName) is { } loaded) return loaded;

        foreach (var assembly in Probed.Value)
            if (Get(assembly, fullName) is { } probed) return probed;

        return Type.GetType(fullName, throwOnError: false);
    }

    private static Type? Get(Assembly assembly, string name)
    {
        try { return assembly.GetType(name, throwOnError: false); }
        catch (Exception) { return null; }
    }

    /// <summary>输出目录程序集探测（.dll 按名加载；已加载者跳过，加载失败者静默跳过）。</summary>
    private static IReadOnlyList<Assembly> LoadProbed()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        var assemblies = new List<Assembly>();
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (loaded.Contains(name)) continue;

            try { assemblies.Add(Assembly.Load(new AssemblyName(name))); }
            catch (Exception) { /* 原生/依赖缺失/重复标识：跳过（未找到类型时由上层给出明确诊断） */ }
        }

        return assemblies;
    }
}
