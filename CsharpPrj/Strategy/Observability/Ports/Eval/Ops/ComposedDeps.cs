using System.Reflection;
using System.Text.Json;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;

/// <summary>
/// 依赖构建单源（组合绑定 <c>deps</c> 与脚本绑定 <c>deps</c>/<c>script[].new</c> 共用；E15 续）：
/// <c>new</c>（反射构造，含开放泛型闭合）｜<c>factory</c>（数据半工厂表）｜<c>fake</c>（代理替身）。
/// <para>分工：本件只认不透明字符串 + 反射（L1 红线），领域语义由声明文本承载。</para>
/// </summary>
internal static class ComposedDeps
{
    /// <summary>按声明序构建依赖（下标引用只能指向更早的依赖：保证构建拓扑无环）。</summary>
    internal static object?[] Build(
        IReadOnlyList<DepStandard> spec, IPortInput input,
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> factories, string portKey)
    {
        var deps = new object?[spec.Count];
        for (var i = 0; i < deps.Length; i++)
            deps[i] = BuildDep(spec, i, input, deps, factories, portKey);

        return deps;
    }

    /// <summary>构建单个依赖（形态三选一：<c>new</c>/<c>factory</c>/<c>fake</c>）。</summary>
    internal static object? BuildDep(
        IReadOnlyList<DepStandard> spec, int index, IPortInput input, object?[] deps,
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> factories, string portKey)
    {
        var dep = spec[index];
        if (dep.Fake is { Length: > 0 } kind) return BuildFake(spec, index, kind, deps, portKey);

        if (dep.New is { Length: > 0 } typeName)
        {
            var type = ResolveType(typeName, dep.GenericArgs);
            var ctor = PickConstructor(portKey, type, index, dep.Args, input, deps);
            var args = ComposedArgs.Build(
                ctor.GetParameters(), dep.Args, input, deps, portKey, $"dep[{index}].new {type.Name}");
            return PortInvocation.Construct(type, args);
        }

        if (dep.Factory is { Length: > 0 } factory)
        {
            if (!factories.TryGetValue(factory, out var builder))
                throw new PortBindingViolationException(
                    portKey,
                    $"端口 '{portKey}' 的 dep[{index}] 引用未注入的工厂 '{factory}'" +
                    $"（已注入：{(factories.Count == 0 ? "(无)" : string.Join(", ", factories.Keys))}）");

            return builder(ComposedArgs.Values(dep.Args, input, deps, portKey, $"dep[{index}].factory"));
        }

        throw new PortBindingViolationException(
            portKey, $"端口 '{portKey}' 的 dep[{index}] 未声明形态（须给 new / factory / fake）");
    }

    /// <summary>
    /// 构建代理替身（计数 / 记录 / 固定应答 / 序列应答 / 不该调用 / 应答脚本）。
    /// <para>
    /// G1：声明了 <c>calls[]</c> 应答脚本时，recording/counting 允许**不包装**真实实现
    /// （即「从零多成员桩」）——应答由脚本给出、调用计数仍由装饰层记录。
    /// </para>
    /// </summary>
    internal static object? BuildFake(
        IReadOnlyList<DepStandard> spec, int index, string kind, object?[] deps, string portKey)
    {
        var dep = spec[index];
        var contract = ContractOf(index, dep, portKey);
        var shaped = ParseKind(kind, index, portKey);
        var calls = CallsOf(index, dep, portKey);

        if (dep.State is not null && shaped != FakeKind.Stateful)
            throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 dep[{index}] 声明了 state 但 fake 种类 '{kind}' 不是 stateful（state 只属有状态替身）");

        if (shaped == FakeKind.Stateful && dep.State is null)
            throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 dep[{index}] 声明 fake='stateful' 但缺 state（须给 write/read/key）");

        var wrapped = dep.Wrap is { } wrap ? Dep(deps, wrap, index, portKey) : null;
        if (calls is null && shaped is FakeKind.Recording or FakeKind.Counting)
            wrapped = Wrapped(index, wrapped, kind, portKey);

        return PortFake.CreateProxy(contract, shaped, new FakeOptions
        {
            Inner = wrapped,
            Methods = dep.Methods,
            Calls = calls,
            CaptureMember = dep.Capture is { } capture ? capture.Member ?? string.Empty : null,
            HasValue = shaped == FakeKind.Fixed,
            Value = shaped == FakeKind.Fixed && dep.Values is { Count: > 0 } ? dep.Values[0] : null,
            Values = shaped == FakeKind.Sequence
                ? (dep.Values ?? Array.Empty<JsonElement>()).Cast<object?>().ToArray()
                : null,
            StateWriteMember = dep.State?.Write,
            StateReadMember = dep.State?.Read,
            StateKeyIndex = dep.State?.Key ?? 0,
        });
    }

    /// <summary>
    /// 替身种类词 → 内核形态（未登记即抛，防静默回落；取值域权威 = 标准词表 <c>PortImplVocabulary.FakeKinds</c>）。
    /// <para>
    /// 数据驱动（按词表成员校验 + 枚举名解析），**不新增形态分派分支** —— 形态分派预算只留给
    /// <c>PortFake</c> 的两载体入口（见 <c>PortKernelDisciplineGuardTests</c>）；词表段经
    /// <see cref="PortImplVocabulary.RenderList"/> 派生（禁手抄清单）。
    /// </para>
    /// </summary>
    private static FakeKind ParseKind(string kind, int index, string portKey)
        => PortImplVocabulary.FakeKinds.Contains(kind, StringComparer.Ordinal)
           && Enum.TryParse(kind, ignoreCase: true, out FakeKind shaped)
            ? shaped
            : throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 dep[{index}] fake 种类 '{kind}' 未知" +
                $"（{PortImplVocabulary.RenderList(PortImplVocabulary.FakeKinds)}）");

    /// <summary>应答脚本转换（标准声明 → 内核脚本项；空 = 不启用）。</summary>
    private static IReadOnlyList<FakeCall>? CallsOf(int index, DepStandard dep, string portKey)
    {
        if (dep.Calls is not { Count: > 0 } calls) return null;

        var shaped = new List<FakeCall>(calls.Count);
        var position = -1;

        foreach (var call in calls)
        {
            position++;

            if (call.Position is < 0)
                throw new PortBindingViolationException(
                    portKey,
                    $"端口 '{portKey}' 的 dep[{index}].calls[{position}] 的 position 为负（调用序自 0 起）");

            shaped.Add(new FakeCall
            {
                Member = call.Member,
                Position = call.Position,
                Value = call.Value?.Clone(),
                HasValue = call.HasValue,
                ThrowType = call.Throw,
                Message = call.Message,
                ValueFactory = call.ValueSource is { } source
                    ? args => CallValueOf(source, args, portKey, index, position)
                    : null,
            });
        }

        return shaped;
    }

    /// <summary>
    /// 参数化应答求值（<c>calls[].value</c> 为取值来源）：以**本次调用实参**为位置实参构造输入，复用脚本取值单源
    /// <see cref="ScriptedValue.Resolve"/>（<c>from:arg</c> 等），禁新增表达式语言。
    /// </summary>
    private static object? CallValueOf(
        ValueSourceStandard source, object?[] args, string portKey, int index, int position)
    {
        var scope = new EvalScope(portKey);
        var input = new PortInput(portKey, args);
        return ScriptedValue.Resolve(source, scope, input, Array.Empty<object?>(), $"dep[{index}].calls[{position}].value");
    }

    /// <summary>包装形态的被装饰对象（recording/counting 必须给出 wrap；否则无法转发）。</summary>
    private static object Wrapped(int index, object? wrapped, string kind, string portKey)
        => wrapped ?? throw new PortBindingViolationException(
            portKey,
            $"端口 '{portKey}' 的 dep[{index}] 声明 fake='{kind}' 但缺 wrap（该形态需装饰一个已有依赖）");

    /// <summary>
    /// 替身契约接口（声明必填：不从运行时类型猜接口，避免多接口时静默择一）。
    /// <para>多契约用 <c>|</c> 分隔（E2-b）：合成继承接口后由单代理承载 ⇒ 同一桩可被任一原契约转换。</para>
    /// </summary>
    private static IReadOnlyList<Type> ContractOf(int index, DepStandard spec, string portKey)
    {
        if (spec.Contract is not { Length: > 0 } name)
            throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 dep[{index}] 声明 fake='{spec.Fake}' 但缺 contract（契约接口全名）");

        return name
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ReflectivePortResolver.ResolveType)
            .ToArray();
    }

    /// <summary>依赖引用（下标必须存在且指向更早的依赖）。</summary>
    internal static object? Dep(object?[] deps, int reference, int index, string portKey)
        => reference >= 0 && reference < deps.Length
            ? deps[reference]
            : throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 dep[{index}] 引用了不可用的依赖下标 {reference}" +
                $"（依赖共 {deps.Length} 个，且只能引用更早的依赖）");

    /// <summary>类型解析（含开放泛型闭合：<c>GenericArgs</c> 按序闭合泛型形参）。</summary>
    internal static Type ResolveType(string typeName, IReadOnlyList<string>? genericArgs)
    {
        var type = ReflectivePortResolver.ResolveType(typeName);
        if (genericArgs is not { Count: > 0 }) return type;

        return type.IsGenericTypeDefinition
            ? type.MakeGenericType(genericArgs.Select(ReflectivePortResolver.ResolveType).ToArray())
            : type;
    }

    /// <summary>
    /// 实例调用的**声明类型**（依赖声明的 <c>contract</c> / <c>new</c>；缺声明即抛）——
    /// 组合绑定与脚本绑定共用的唯一实现。
    /// </summary>
    internal static Type DeclaredInstanceType(
        IReadOnlyList<DepStandard> specs, int index, string portKey, string context)
    {
        var typeName = specs[index].Contract ?? specs[index].New;
        if (typeName is not { Length: > 0 })
            throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 {context} 对 dep[{index}] 取实例成员，但该依赖未声明 contract / new（实例类型不可知）");

        return ResolveType(typeName, specs[index].GenericArgs);
    }

    /// <summary>构造选择（组合绑定 <c>deps.new</c> 形态：带声明实参 → 按可绑定度择优，E1）。</summary>
    internal static ConstructorInfo PickConstructor(
        string portKey, Type type, int index, IReadOnlyList<ComposedArgStandard>? declared,
        IPortInput input, object?[] deps)
        => PickConstructor(
            portKey, type, declared?.Count ?? 0, $"dep[{index}]",
            declared is not { Count: > 0 }
                ? null
                : constructor => ComposedArgs.Build(
                    constructor.GetParameters(), declared, input, deps, portKey, $"dep[{index}]"),
            ComposedArgs.Describe(declared));

    /// <summary>
    /// 构造选择（E1：**按实参可绑定度择优**）：候选集 = **可容纳区间**（必填数 ≤ 声明数 ≤ 形参数）；命名绑定
    /// （<c>named</c>）时个数不参与判别（候选取全集）。逐候选试绑定（<paramref name="tryBind"/> 返 null / 抛绑定异常
    /// 即「不可绑定」）→ 精确类型命中 &gt; 可归一 &gt; 形参缺省，并对「靠缺省填满」的候选降权；唯一最高分选它；
    /// **并列最高分 / 全部不可绑定 / 无可容纳候选 → fail-fast 并列出候选签名与逐候选拒绝原因**（防静默绑错：D1 消除
    /// 「回落首个构造函数 + CLR 默认值填充」导致的语义反转）。
    /// <para>无 <paramref name="tryBind"/> 时仅在**唯一可容纳候选**下直取；多个候选则 fail-fast（摘要候选签名，不猜）。</para>
    /// </summary>
    internal static ConstructorInfo PickConstructor(
        string portKey, Type type, int declaredCount, string context,
        Func<ConstructorInfo, object?[]?>? tryBind = null, string? declaredText = null, bool namedBinding = false)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Length == 0)
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context}：类型 {type.Name} 上没有公开构造函数");

        var described = $"实参 [{declaredText ?? $"{declaredCount} 个"}]";
        var candidates = namedBinding ? constructors : Fittable(constructors, declaredCount);

        if (candidates.Length == 0)
            throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 {context}（{type.Name}）无可容纳声明实参数的构造函数：" +
                $"候选 {Signatures(constructors)}；{described}（可容纳 = 必填数 ≤ 声明数 ≤ 形参数）");

        if (tryBind is null)
        {
            // 无实参判别依据：优先「形参数 == 声明数」的精确候选；仍非唯一 → fail-fast（不猜）。
            var exact = candidates.Where(c => c.GetParameters().Length == declaredCount).ToArray();
            var pick = exact.Length > 0 ? exact : candidates;

            return pick.Length == 1
                ? pick[0]
                : throw new PortBindingViolationException(
                    portKey,
                    $"端口 '{portKey}' 的 {context}（{type.Name}）构造并列（无可绑定度判别依据）：" +
                    $"候选 {Signatures(pick)}；{described}");
        }

        ConstructorInfo? best = null;
        var bestScore = 0;
        var tied = 0;
        var rejected = new List<string>();

        foreach (var candidate in candidates)
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

            if (score < 0)
            {
                rejected.Add($"{Signature(candidate)} ⇒ 实参与形参类型不可绑定");
                continue;
            }

            // 首个可绑定候选即基准（不得用哨兵初值：得分恰为哨兵值时会与之「并列」而永不入选；降权后得分可为负）。
            if (best is null)
            {
                best = candidate;
                bestScore = score;
                tied = 1;
                continue;
            }

            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
                tied = 1;
            }
            else if (score == bestScore)
            {
                tied++;
            }
        }

        if (best is null)
            throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 {context}（{type.Name}）无可绑定构造：候选 {Signatures(candidates)}；{described}"
                + Reasons(rejected));

        if (tied > 1)
            throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 {context}（{type.Name}）构造并列歧义：候选 {Signatures(candidates)}；{described}");

        return best;
    }

    /// <summary>逐候选拒绝原因段（诊断单源：无拒绝记录则为空串）。</summary>
    private static string Reasons(IReadOnlyList<string> rejected)
        => rejected.Count == 0
            ? string.Empty
            : $"{Environment.NewLine}逐候选试绑定原因：" + string.Join(Environment.NewLine, rejected);

    /// <summary>单候选签名（与 <see cref="Signatures"/> 同口径）。</summary>
    private static string Signature(ConstructorInfo candidate) => Signatures([candidate]);

    /// <summary>
    /// 可容纳声明实参数的构造函数候选（区间：必填数 ≤ 声明数 ≤ 形参数；按总形参数升序 = 最少补齐优先）。
    /// </summary>
    private static ConstructorInfo[] Fittable(ConstructorInfo[] constructors, int declaredCount)
        => constructors
            .Where(c => PortValues.Required(c.GetParameters()) <= declaredCount
                        && declaredCount <= c.GetParameters().Length)
            .OrderBy(c => c.GetParameters().Length)
            .ToArray();

    /// <summary>候选签名清单（诊断文案：<c>(Int32 capacity) / (String value)</c> 形态）。</summary>
    private static string Signatures(IEnumerable<ConstructorInfo> candidates)
        => string.Join(" / ", candidates.Select(candidate => $"({string.Join(", ",
            candidate.GetParameters().Select(parameter => $"{parameter.ParameterType.Name} {parameter.Name}"))})"));
}
