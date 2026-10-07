using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>
/// 反射端口绑定的解析小件（类型 / 方法 / 参数默认值 / JSON 反序列化）：分离后便于守卫与单测直接锁定。
/// <para>批 1 收敛：<c>ReflectiveBinding</c> 薄适配类随 ③ 家族退场（反射族一律经求值引擎），本件保留为**解析唯一实现**。</para>
/// </summary>
internal static class ReflectivePortResolver
{
    // 并发缓存：绑定实例在端口目录中为单例、可被多线程并发调用，解析缓存须并发安全（禁非线程安全 Dictionary）。
    private static readonly ConcurrentDictionary<string, Type> TypeCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, MethodInfo[]> MethodCache = new(StringComparer.Ordinal);

    /// <summary>
    /// 解析目标类型：已加载程序集 → 输出目录探测（宿主按二进制引用装配，目标程序集可能尚未加载）→ <c>Type.GetType</c>。
    /// <para>
    /// 支持泛型语法全名（E2）：<c>Ns.Type&lt;A,B&gt;</c>（实参递归解析，含嵌套泛型）、<c>T[]</c>、<c>T?</c>
    /// （值类型视为 <c>Nullable&lt;T&gt;</c>）、嵌套类型 <c>Outer.Inner</c>；本件为类型解析**唯一实现**
    /// （<see cref="ComposedDeps.ResolveType"/> 的「开放泛型 + 参数表」形态经本件转发）。
    /// </para>
    /// </summary>
    internal static Type ResolveType(string fullName)
    {
        if (TypeCache.TryGetValue(fullName, out var cached)) return cached;

        var type = TryResolveType(fullName, out var deepest)
            ?? throw new InvalidOperationException(
                $"[port:bind] 目标类型 '{fullName}' 未加载（已解析到最深片段 '{deepest}'；" +
                $"参与查找的程序集 {PortTypeLoader.SearchedAssemblyCount} 个；检查工程二进制引用与命名空间）");

        TypeCache[fullName] = type;
        return type;
    }

    /// <summary>类型解析（可失败形态：返回 null 并给出「已解析到的最深片段」供诊断）。</summary>
    private static Type? TryResolveType(string text, out string deepest)
    {
        var name = text.Trim();
        deepest = name;
        if (name.Length == 0) return null;

        if (name.EndsWith("[]", StringComparison.Ordinal))
        {
            var element = TryResolveType(name[..^2], out deepest);
            return element?.MakeArrayType();
        }

        if (name.EndsWith('?'))
        {
            var inner = TryResolveType(name[..^1], out deepest);
            if (inner is null) return null;

            return inner.IsValueType ? typeof(Nullable<>).MakeGenericType(inner) : inner;
        }

        var open = name.IndexOf('<', StringComparison.Ordinal);
        if (open >= 0 && name.EndsWith('>'))
        {
            var head = name[..open];
            if (SplitGenericArguments(name[(open + 1)..^1]) is not { } arguments) return null;

            var definition = FindType($"{head}`{arguments.Count}");
            if (definition is null) return null;

            var closed = new Type[arguments.Count];
            for (var i = 0; i < arguments.Count; i++)
            {
                var argument = TryResolveType(arguments[i], out var fragment);
                if (argument is null)
                {
                    deepest = fragment;
                    return null;
                }

                closed[i] = argument;
            }

            try
            {
                return definition.MakeGenericType(closed);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        if (KeywordAliases.TryGetValue(name, out var alias)) return alias;

        return FindType(name);
    }

    /// <summary>C# 关键字别名 → CLR 类型（E2：声明面按 C# 语法书写泛型实参，如 <c>Func&lt;object,Task&lt;object&gt;&gt;</c>）。</summary>
    private static readonly Dictionary<string, Type> KeywordAliases = new(StringComparer.Ordinal)
    {
        ["object"] = typeof(object),
        ["string"] = typeof(string),
        ["bool"] = typeof(bool),
        ["char"] = typeof(char),
        ["byte"] = typeof(byte),
        ["sbyte"] = typeof(sbyte),
        ["short"] = typeof(short),
        ["ushort"] = typeof(ushort),
        ["int"] = typeof(int),
        ["uint"] = typeof(uint),
        ["long"] = typeof(long),
        ["ulong"] = typeof(ulong),
        ["float"] = typeof(float),
        ["double"] = typeof(double),
        ["decimal"] = typeof(decimal),
        ["void"] = typeof(void),
    };

    /// <summary>按全名 / 嵌套名查找类型（嵌套类型由 <c>Outer.Inner</c> 逐级回落 <c>Outer+Inner</c>）。</summary>
    private static Type? FindType(string name)
    {
        if (PortTypeLoader.Find(name) is { } type) return type;

        for (var index = name.LastIndexOf('.'); index > 0; index = name.LastIndexOf('.', index - 1))
        {
            var nested = $"{name[..index]}+{name[(index + 1)..]}";
            if (PortTypeLoader.Find(nested) is { } nestedType) return nestedType;
        }

        return null;
    }

    /// <summary>泛型实参表切分（按最外层逗号；括号不配对 → null）。</summary>
    private static IReadOnlyList<string>? SplitGenericArguments(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    if (depth < 0) return null;
                    break;
                case ',' when depth == 0:
                    parts.Add(text[start..i]);
                    start = i + 1;
                    break;
            }
        }

        if (depth != 0) return null;

        parts.Add(text[start..]);

        var trimmed = parts.Select(part => part.Trim()).ToArray();
        return trimmed.Any(part => part.Length == 0) ? null : trimmed;
    }

    /// <summary>
    /// 解析目标方法：优先「公开 + 参数个数 = 声明个数」的候选；无精确匹配则取参数最少者（尾部参数按默认值补齐）。
    /// 同名方法零候选即抛（防静默绑错）。
    /// </summary>
    internal static MethodInfo ResolveMethod(string portKey, string methodName, int declaredCount, Type type)
        => ResolveMethod(type, portKey, methodName, declaredCount, isStatic: true);

    /// <summary>
    /// 方法解析（声明式组合绑定用：同一口径支持静态 / 实例两种绑定标志；无实参判别 → 精确个数 → 位置个数 → **区间容纳**）。
    /// </summary>
    internal static MethodInfo ResolveMethod(
        Type type, string portKey, string methodName, int declaredCount, bool isStatic, bool nonPublic = false)
    {
        var candidates = ResolveMethodCandidates(type, portKey, methodName, isStatic, nonPublic);

        return candidates.FirstOrDefault(m => DeclaredCount(m) == declaredCount)
               ?? candidates.FirstOrDefault(m => m.GetParameters().Length == declaredCount)
               ?? Legacy(candidates, declaredCount, portKey, $"调用 '{methodName}'", type, methodName);
    }

    /// <summary>
    /// 方法解析**择优形态**（E9：多候选按实参可绑定度择一）：候选集 = 参数个数 == 声明个数（非 <c>out</c> 优先，
    /// 回落总个数，再回落**区间容纳**）；逐候选试绑定（<paramref name="tryBind"/> 失败即「不可绑定」）→ 唯一最高分选它；
    /// **并列最高分 → 保持旧口径（首个参数个数匹配者）**（产品侧已硬约束「不得同名同参数个数重载」，故并列不出现；
    /// 出现时保持旧行为，避免无谓回归）；全部不可绑定 → fail-fast 并列候选签名 + 逐候选原因。
    /// <para>
    /// <paramref name="genericArity"/> 非空 = 声明了 <c>genericArgs</c>：候选集**先收敛到同元数泛型定义**
    /// （否则非泛型重载可能以「形参个数相同」混入并被选中，闭合时即报「不是开放泛型方法」）。
    /// </para>
    /// </summary>
    internal static MethodInfo ResolveMethod(
        Type type, string portKey, string methodName, int declaredCount, bool isStatic, bool nonPublic,
        Func<MethodInfo, object?[]?> tryBind, string context, int? genericArity = null, bool namedBinding = false)
    {
        var candidates = ResolveMethodCandidates(type, portKey, methodName, isStatic, nonPublic);
        var universe = genericArity is { } arity
            ? candidates.Where(m => m.IsGenericMethodDefinition && m.GetGenericArguments().Length == arity).ToArray()
            : candidates;

        // 命名绑定（`named` = 形参名）时声明个数不构成判别依据（形参按名对位，可超过声明个数）⇒ 候选集不按个数过滤，
        // 错因由绑定期给出（含未知名），避免 arity 前置过滤把精确诊断挡掉。
        var compatible = namedBinding
            ? universe
            : universe.Where(m => DeclaredCount(m) == declaredCount).ToArray();

        if (!namedBinding && compatible.Length == 0)
            compatible = universe.Where(m => m.GetParameters().Length == declaredCount).ToArray();

        // 第三级：区间容纳（必填数 ≤ 声明数 ≤ 形参数，最少补齐优先）——使「尾部可选形参」重载可被选中，
        // 而不是让下游以 DefaultFor 静默补齐一个容纳不了的候选（D1 同口径）。
        if (!namedBinding && compatible.Length == 0) compatible = Fittable(universe, declaredCount);

        if (compatible.Length <= 1)
            return compatible.FirstOrDefault()
                   ?? Legacy(universe, declaredCount, portKey, context, type, methodName, namedBinding);

        MethodInfo? best = null;
        var bestScore = 0;
        var tied = 0;
        var rejected = new List<string>();

        foreach (var candidate in compatible)
        {
            object?[]? bound;
            try
            {
                bound = tryBind(candidate);
            }
            catch (PortBindingViolationException ex)
            {
                rejected.Add($"{Signature(candidate)} ⇒ {ex.Message}");
                continue;
            }

            if (bound is null)
            {
                rejected.Add($"{Signature(candidate)} ⇒ 实参不可绑定（试绑定拒绝）");
                continue;
            }

            var score = PortValues.ScoreArguments(candidate.GetParameters(), bound, declaredCount);

            // 首个可绑定候选即基准（不得用 -1 之类哨兵初值：得分恰为哨兵值时会与之「并列」而永不入选）。
            if (best is null)
            {
                best = candidate;
                bestScore = score;
                tied = 1;
                continue;
            }

            if (score < bestScore) continue;
            if (score == bestScore)
            {
                tied++;
                continue;
            }

            best = candidate;
            bestScore = score;
            tied = 1;
        }

        if (best is null)
            throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 {context}：类型 {type.Name} 上无可绑定方法 {methodName}（候选 {Signatures(compatible)}）"
                + (rejected.Count > 0
                    ? $"{Environment.NewLine}逐候选试绑定原因：" + string.Join(Environment.NewLine, rejected)
                    : string.Empty));

        return tied > 1 ? Legacy(compatible, declaredCount, portKey, context, type, methodName, namedBinding) : best;
    }

    /// <summary>同名方法候选集（缓存：择优所需的实参判别在调用侧完成，故缓存的是候选集而非选择结果）。</summary>
    internal static MethodInfo[] ResolveMethodCandidates(
        Type type, string portKey, string methodName, bool isStatic, bool nonPublic)
    {
        var cacheKey = $"{type.FullName}.{methodName}//{(isStatic ? "s" : "i")}/{(nonPublic ? "n" : "p")}";
        if (MethodCache.TryGetValue(cacheKey, out var cached)) return cached;

        var flags = BindingFlags.Public | BindingFlags.FlattenHierarchy
                    | (nonPublic ? BindingFlags.NonPublic : BindingFlags.Public)
                    | (isStatic ? BindingFlags.Static : BindingFlags.Instance);

        var candidates = type
            .GetMethods(flags)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
            throw new InvalidOperationException(
                $"[port:bind] 端口 '{portKey}'：类型 {type.Name} 上找不到方法 {methodName}" +
                $"（可用：{AvailableNames(type, flags)}）");

        MethodCache[cacheKey] = candidates;
        return candidates;
    }

    /// <summary>
    /// 旧口径选择（参数个数精确匹配 → 位置个数匹配 → **区间容纳**：必填数 ≤ 声明数 ≤ 形参数，取最小总形参数者）；
    /// 三者皆无 → fail-fast 列候选签名（替代旧的「取参数最少者」静默绑错：旧口径会把声明实参数喂给一个容纳不了的
    /// 重载，尾部形参被 <see cref="DefaultFor(Type)"/> 静默补齐 ⇒ 语义反转；D1 同口径）。
    /// <para><paramref name="namedBinding"/> 时个数不参与判别 → 保持旧「最小形参数」择一，错因由绑定期给出（含未知名）。</para>
    /// </summary>
    private static MethodInfo Legacy(
        MethodInfo[] candidates, int declaredCount, string portKey, string context, Type type, string methodName,
        bool namedBinding = false)
    {
        var selected = candidates.FirstOrDefault(m => DeclaredCount(m) == declaredCount)
                       ?? candidates.FirstOrDefault(m => m.GetParameters().Length == declaredCount);

        if (selected is not null) return selected;

        if (namedBinding) return candidates.OrderBy(m => m.GetParameters().Length).First();

        var fittable = Fittable(candidates, declaredCount);
        if (fittable.Length > 0) return fittable[0];

        throw new PortBindingViolationException(
            portKey,
            $"端口 '{portKey}' 的 {context}：类型 {type.Name} 上无方法 {methodName} 可容纳声明实参数 {declaredCount}" +
            $"（候选 {Signatures(candidates)}）");
    }

    /// <summary>可容纳声明实参数的方法候选（区间：必填数 ≤ 声明数 ≤ 形参数；按总形参数升序 = 最少补齐优先）。</summary>
    private static MethodInfo[] Fittable(MethodInfo[] candidates, int declaredCount)
        => candidates
            .Where(m => RequiredCount(m) <= declaredCount && declaredCount <= m.GetParameters().Length)
            .OrderBy(m => m.GetParameters().Length)
            .ToArray();

    /// <summary>必填形参数（<c>out</c> 不计：出参由捕获写槽，声明面不为它写实参；有默认值者不计）。</summary>
    private static int RequiredCount(MethodInfo method)
        => method.GetParameters().Count(p => !p.IsOut && !p.HasDefaultValue && !p.IsOptional);

    /// <summary>候选签名清单（诊断：`(Int32 capacity)` 形态）。</summary>
    private static string Signatures(IEnumerable<MethodInfo> candidates)
        => string.Join(" / ", candidates.Select(candidate => $"({string.Join(", ",
            candidate.GetParameters().Select(parameter => $"{parameter.ParameterType.Name} {parameter.Name}"))})"));

    /// <summary>单候选签名（逐候选试绑定原因诊断用；与 <see cref="Signatures"/> 同口径）。</summary>
    private static string Signature(MethodInfo candidate) => Signatures([candidate]);

    /// <summary>可用方法清单（E9 诊断：方法缺失时给出同名方法与其重载签名，有界限长）。</summary>
    private static string AvailableNames(Type type, BindingFlags flags)
    {
        const int limit = 12;

        var groups = type.GetMethods(flags)
            .Where(method => !method.IsSpecialName)
            .GroupBy(method => method.Name, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        if (groups.Length == 0) return "(无)";

        var listed = groups.Take(limit)
            .Select(group => $"{group.Key}{Signatures(group.OrderBy(m => m.GetParameters().Length))}")
            .ToArray();

        return groups.Length > limit
            ? string.Join(", ", listed) + $" …（共 {groups.Length} 个方法名）"
            : string.Join(", ", listed);
    }

    /// <summary>
    /// 声明实参数对应的方法形参数：<c>out</c> 形参不计（<c>invoke.out</c> 由出参捕获按位写入槽，
    /// 声明面不为出参写实参）——见《…声明式编排框架》§9.1 试点写法。
    /// </summary>
    private static int DeclaredCount(MethodInfo method)
        => method.GetParameters().Count(parameter => !parameter.IsOut);

    /// <summary>成员读取解析（属性优先、缺失回落静态 / 实例字段）：属性与字段是产品表示选择，声明侧不区分。
    /// <para><paramref name="nonPublic"/> = true 时按「公开 + 非公开」解析（W20：产品未公开入口的结构探针）。</para>
    /// </summary>
    internal static MemberInfo ResolveMember(Type type, string portKey, string name, bool isStatic, bool nonPublic = false)
    {
        var flags = BindingFlags.Public | BindingFlags.FlattenHierarchy
                    | (nonPublic ? BindingFlags.NonPublic : BindingFlags.Public)
                    | (isStatic ? BindingFlags.Static : BindingFlags.Instance);

        return (MemberInfo?)type.GetProperty(name, flags)
               ?? type.GetField(name, flags)
               ?? throw new InvalidOperationException(
                   $"[port:bind] 端口 '{portKey}'：类型 {type.Name} 上找不到成员 {name}");
    }

    /// <summary>
    /// 成员读取唯一入口（属性优先、字段；静态成员 <paramref name="instance"/> 传 null）：
    /// 全端口目录成员取值共用（调用算子 / 脚本调用 / 取值表达式 / restore 留存 / 属性路径）。
    /// <see cref="ResolveMember"/> 只可能产出属性或字段，其余成员即内部不变量破坏（抛 <see cref="InvalidOperationException"/>）。
    /// </summary>
    internal static object? ReadMember(MemberInfo member, object? instance) => member switch
    {
        PropertyInfo property => property.GetValue(instance),
        FieldInfo field => field.GetValue(instance),
        _ => throw new InvalidOperationException(
            $"成员 '{member.DeclaringType?.Name}.{member.Name}' 不是可读属性 / 字段（实际 {member.GetType().Name}）"),
    };

    /// <summary>成员类型（属性 / 字段；与 <see cref="ResolveMember"/> 的产出域一致）。</summary>
    internal static Type MemberType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => throw new InvalidOperationException(
            $"成员 '{member.DeclaringType?.Name}.{member.Name}' 不是属性 / 字段（实际 {member.GetType().Name}）"),
    };

    /// <summary>
    /// 成员写入唯一入口（属性优先、字段；静态成员 <paramref name="instance"/> 传 null）：
    /// 供 <c>restore</c> 作用域的进入期设值 / 退出期自动还原（NG-2）共用。
    /// </summary>
    internal static void WriteMember(MemberInfo member, object? instance, object? value)
    {
        switch (member)
        {
            case PropertyInfo property:
                property.SetValue(instance, value);
                return;
            case FieldInfo field:
                field.SetValue(instance, value);
                return;
            default:
                throw new InvalidOperationException(
                    $"成员 '{member.DeclaringType?.Name}.{member.Name}' 不是可写属性 / 字段（实际 {member.GetType().Name}）");
        }
    }

    /// <summary>目标形参的默认值（值类型 → 默认实例；<c>CancellationToken</c> → None；引用类型 → null）。</summary>
    internal static object? DefaultFor(Type type)
        => type == typeof(CancellationToken)
            ? CancellationToken.None
            : type.IsValueType ? Activator.CreateInstance(type) : null;

    /// <summary>
    /// 形参默认值单源（D1）：**先取声明的 <see cref="ParameterInfo.DefaultValue"/>**（<c>= true</c> 之类的可选参数
    /// 按其声明默认值补齐，禁以 CLR 默认值顶替 ⇒ 消除「语义反转」），无声明默认值再回落
    /// <see cref="DefaultFor(Type)"/>。
    /// <para><c>CancellationToken</c> 不取声明默认值：其默认值恒为 <c>default</c>，语义上与 <c>None</c> 等价但类型不可直投。</para>
    /// </summary>
    internal static object? DefaultFor(ParameterInfo parameter)
    {
        if (parameter.ParameterType == typeof(CancellationToken)) return CancellationToken.None;

        if (parameter.HasDefaultValue && parameter.DefaultValue is { } declared
            && declared is not DBNull && declared is not Missing)
            return declared;

        return DefaultFor(parameter.ParameterType);
    }

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

    /// <summary>参与类型查找的程序集数（诊断信息：已加载 + 输出目录探测）。</summary>
    internal static int SearchedAssemblyCount => AppDomain.CurrentDomain.GetAssemblies().Length + Probed.Value.Count;

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
