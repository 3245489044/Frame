using System.Reflection;
using System.Text;

namespace DataCenter.Standards;

/// <summary>
/// 映射配对：<paramref name="Standard"/> 为**唯一数据源**（标准模型），
/// <paramref name="MapperTarget"/>（可选）为「标准 → 内核声明」映射的构造目标类型。
/// <para>
/// 方法名为政策：<paramref name="MapperMethod"/> = 「标准 → 目标」方法名（<paramref name="MapperTarget"/> 非空时必需）。
/// </para>
/// <para>
/// 归位批 I2 收敛：镜像面（<c>*Dto</c>）与「标准 → 镜像」投影随 DTO 退场删除 ——
/// 生成源直接是标准模型，故不再需要 <c>Mirror</c> / <c>ProjectMethod</c> 两字段。
/// </para>
/// </summary>
public sealed record MirrorPair(
    Type Standard,
    Type? MapperTarget = null,
    string? MapperMethod = null);

/// <summary>
/// 映射生成计划（政策）：配对表。**生成源 = 标准模型**（<see cref="MirrorPair.Standard"/> 的主构造形参），
/// 故字段名 / 位序 / 可空性 / 默认值一律由源派生（手改生成物即被下次生成覆盖 = 生成物纪律）。
/// </summary>
public sealed record MirrorPlan(IReadOnlyList<MirrorPair> Pairs);

/// <summary>
/// 镜像生成的**前置条件违例**：仅发生于生成期（离线工具 / 守卫的显式再生成通道），
/// 与装载期诊断无关（装载期一律 <see cref="StandardViolationException"/>；标准层零 IO、不写盘）。
/// </summary>
public sealed class MirrorEmitException(string message) : Exception(message);

/// <summary>
/// 记录**源码生成机制**（框架件）：由标准模型（源）机械派生「标准 → 内核声明」映射产物。
/// <para>
/// 纪律（与 <see cref="SchemaEnumSynchronizer"/> 同族）：① 本件**零 IO、永不写盘**（写盘只在离线工具 / 守卫的显式通道）；
/// ② 只做「按位搬运 + 嵌套递归 + 集合投影」，**不含任何领域语义**（类型 / 方法名 / 命名空间全由计划注入）；
/// ③ 源与目标的主构造形参**位序即语义**（数量不等即抛，禁静默错位）。
/// </para>
/// </summary>
public static class StandardMirrorEmit
{
    /// <summary>可空性读取（引用类型的 <c>?</c> 由声明注解决定，不能只靠默认值推断）。</summary>
    private static readonly NullabilityInfoContext Nullability = new();

    /// <summary>「标准 → 目标」映射成员（目标 = 内核声明载体；目标槽位非配对时按位搬运）。</summary>
    public static string EmitMapper(MirrorPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var members = new StringBuilder();
        foreach (var pair in plan.Pairs)
        {
            if (pair.MapperTarget is null || string.IsNullOrWhiteSpace(pair.MapperMethod))
                continue;

            members.Append("/// <summary>").Append(pair.Standard.Name).Append(" → ").Append(pair.MapperTarget.Name)
                .Append("（生成物）。</summary>").AppendLine();
            members.Append("public static ").Append(pair.MapperTarget.Name).Append(' ')
                .Append(pair.MapperMethod).Append('(').Append(pair.Standard.Name).Append(" d) => new(").AppendLine();

            members.Append(Body(
                plan,
                Slots(pair.Standard),
                Slots(pair.MapperTarget),
                sourceVariable: "d"));

            members.AppendLine();
        }

        return Indent(members.ToString().TrimEnd()) + Environment.NewLine;
    }

    /// <summary>主构造形参视图（类型 + 可空性 + 名；位序即语义）。</summary>
    private static (Type Type, NullabilityInfo Info, string Name)[] Slots(Type type)
        => Positional(type)
            .Select(parameter => (parameter.ParameterType, Nullability.Create(parameter), parameter.Name!))
            .ToArray();

    /// <summary>构造实参序列（源槽位 ↔ 目标槽位**逐位**对应）。</summary>
    private static string Body(
        MirrorPlan plan,
        (Type Type, NullabilityInfo Info, string Name)[] source,
        (Type Type, NullabilityInfo Info, string Name)[] target,
        string sourceVariable)
    {
        if (source.Length != target.Length)
            throw new MirrorEmitException(
                "[mirror:emit] 标准面与对标面的主构造形参数量不等："
                + $"{source.Length} vs {target.Length}（位序即语义，禁静默错位）");

        var lines = new StringBuilder();
        for (var index = 0; index < source.Length; index++)
        {
            var (sourceType, sourceInfo, sourceName) = source[index];
            var accessor = sourceVariable + "." + sourceName;
            var expression = Slot(plan, target[index].Type, sourceType, sourceInfo, accessor);

            lines.Append("    ").Append(expression)
                .Append(index == source.Length - 1 ? ");" : ",").AppendLine();
        }

        return lines.ToString();
    }

    /// <summary>
    /// 单槽位表达式：目标槽位是配对类型（或其只读集合 / 只读映射）→ 递归调用其方法；否则原样搬运。
    /// </summary>
    private static string Slot(
        MirrorPlan plan,
        Type targetType,
        Type sourceType,
        NullabilityInfo sourceInfo,
        string accessor)
    {
        var nullable = Nullable.GetUnderlyingType(sourceType) is not null
                       || sourceInfo.ReadState == NullabilityState.Nullable;

        if (MethodOf(plan, targetType) is { } nested)
            return nullable ? $"{accessor} is null ? null : {nested}({accessor})" : $"{nested}({accessor})";

        if (targetType.IsGenericType && Nullable.GetUnderlyingType(targetType) is null)
        {
            var definition = targetType.GetGenericTypeDefinition();
            var arguments = targetType.GetGenericArguments();

            if (arguments.Length == 1 && MethodOf(plan, arguments[0]) is { } element)
                return nullable
                    ? $"{accessor}?.Select({element}).ToArray()"
                    : $"{accessor}.Select({element}).ToArray()";

            if (definition == typeof(IReadOnlyDictionary<,>)
                && MethodOf(plan, arguments[1]) is { } value)
            {
                return nullable
                    ? $"{accessor}?.ToDictionary(kv => kv.Key, kv => {value}(kv.Value), StringComparer.Ordinal)"
                    : $"{accessor}.ToDictionary(kv => kv.Key, kv => {value}(kv.Value), StringComparer.Ordinal)";
            }
        }

        return accessor;
    }

    /// <summary>目标槽位所属的配对方法名（<see cref="MirrorPair.MapperTarget"/> 命中即取其 <see cref="MirrorPair.MapperMethod"/>）。</summary>
    private static string? MethodOf(MirrorPlan plan, Type targetType)
    {
        var candidate = Nullable.GetUnderlyingType(targetType) ?? targetType;

        foreach (var pair in plan.Pairs)
            if (pair.MapperTarget == candidate)
                return pair.MapperMethod;

        return null;
    }

    /// <summary>成员缩进（生成文本自带缩进，故包装类只出大括号）。</summary>
    private static string Indent(string text)
    {
        var result = new StringBuilder(text.Length);

        foreach (var line in text.Split('\n'))
            if (line.Length != 0)
                result.Append("    ").Append(line).Append('\n');

        return result.ToString();
    }

    /// <summary>主构造形参（位置记录：公开实例构造中形参最多者；记录副本构造非 public，故此处恰得一个）。</summary>
    private static ParameterInfo[] Positional(Type type)
        => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .First()
            .GetParameters();
}
