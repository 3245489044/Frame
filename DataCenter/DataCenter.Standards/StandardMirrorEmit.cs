using System.Globalization;
using System.Reflection;
using System.Text;

namespace DataCenter.Standards;

/// <summary>
/// 镜像配对（政策注入）：<paramref name="Mirror"/> ↔ <paramref name="Standard"/> 为镜像面，
/// <paramref name="MapperTarget"/>（可选）为「镜像 → 目标」映射的构造目标类型。
/// <para>
/// 方法名为政策：<paramref name="ProjectMethod"/> = 「标准 → 镜像」方法名（生成进投影面）；
/// <paramref name="MapperMethod"/> = 「镜像 → 目标」方法名（<paramref name="MapperTarget"/> 非空时必需）。
/// </para>
/// </summary>
public sealed record MirrorPair(
    Type Mirror,
    Type Standard,
    string ProjectMethod,
    Type? MapperTarget = null,
    string? MapperMethod = null);

/// <summary>
/// 镜像生成计划（政策）：命名空间 + 配对表。**生成源 = 标准模型**（<see cref="MirrorPair.Standard"/> 的主构造形参），
/// 故字段名 / 位序 / 可空性 / 默认值一律由源派生（手改镜像即被下次生成覆盖 = 生成物纪律）。
/// </summary>
public sealed record MirrorPlan(
    string MirrorNamespace,
    string StandardNamespace,
    IReadOnlyList<MirrorPair> Pairs);

/// <summary>
/// 镜像生成的**前置条件违例**：仅发生于生成期（离线工具 / 守卫的显式再生成通道），
/// 与装载期诊断无关（装载期一律 <see cref="StandardViolationException"/>；标准层零 IO、不写盘）。
/// </summary>
public sealed class MirrorEmitException(string message) : Exception(message);

/// <summary>
/// 记录镜像**源码生成机制**（框架件；依据《DataCenter-标准单源化-重复逻辑收敛-设计与实施》§6-P3-D 的 D-1 生成式）：
/// 由标准模型（源）机械派生三份产物 ——
/// ① <see cref="EmitDeclarations"/>：中立镜像的记录声明；② <see cref="EmitProjection"/>：「标准 → 镜像」逐位投影成员；
/// ③ <see cref="EmitMapper"/>：「镜像 → 目标」逐位映射成员。
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

    /// <summary>镜像记录声明（逐条由标准模型的主构造形参派生；位序 / 名称 / 可空性 / 默认值同源）。</summary>
    public static string EmitDeclarations(MirrorPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var text = new StringBuilder();
        foreach (var pair in plan.Pairs)
        {
            text.Append("/// <summary>镜像 <c>").Append(pair.Standard.Name)
                .Append("</c>（生成物：源 = ").Append(plan.StandardNamespace).Append('.').Append(pair.Standard.Name)
                .Append("；禁手改）。</summary>").AppendLine();
            text.Append("public sealed record ").Append(pair.Mirror.Name).Append('(').AppendLine();

            var parameters = Positional(pair.Standard);
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                text.Append("    ")
                    .Append(RenderType(parameter.ParameterType, Nullability.Create(parameter), plan, standardToMirror: true))
                    .Append(' ').Append(parameter.Name);

                if (parameter.HasDefaultValue)
                    text.Append(" = ").Append(DefaultLiteral(parameter));

                text.Append(index == parameters.Length - 1 ? ");" : ",").AppendLine();
            }

            text.AppendLine();
        }

        return text.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>「标准 → 镜像」投影成员（生成进投影面的 <c>partial</c> 半；方法名由计划给出）。</summary>
    public static string EmitProjection(MirrorPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var members = new StringBuilder();
        foreach (var pair in plan.Pairs)
        {
            members.Append("/// <summary>").Append(pair.Standard.Name).Append(" → ").Append(pair.Mirror.Name)
                .Append("（生成物）。</summary>").AppendLine();
            members.Append("private static ").Append(pair.Mirror.Name).Append(' ')
                .Append(pair.ProjectMethod).Append('(').Append(pair.Standard.Name).Append(" s) => new(").AppendLine();

            members.Append(Body(
                plan,
                Slots(pair.Standard),
                Slots(pair.Mirror),
                sourceVariable: "s",
                fromStandard: true));

            members.AppendLine();
        }

        return Indent(members.ToString().TrimEnd()) + Environment.NewLine;
    }

    /// <summary>「镜像 → 目标」映射成员（目标 = 内核声明载体；目标槽位非配对时按位搬运）。</summary>
    public static string EmitMapper(MirrorPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var members = new StringBuilder();
        foreach (var pair in plan.Pairs)
        {
            if (pair.MapperTarget is null || string.IsNullOrWhiteSpace(pair.MapperMethod))
                continue;

            members.Append("/// <summary>").Append(pair.Mirror.Name).Append(" → ").Append(pair.MapperTarget.Name)
                .Append("（生成物）。</summary>").AppendLine();
            members.Append("public static ").Append(pair.MapperTarget.Name).Append(' ')
                .Append(pair.MapperMethod).Append('(').Append(pair.Mirror.Name).Append(" d) => new(").AppendLine();

            members.Append(Body(
                plan,
                Slots(pair.Mirror),
                Slots(pair.MapperTarget),
                sourceVariable: "d",
                fromStandard: false));

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
        string sourceVariable,
        bool fromStandard)
    {
        if (source.Length != target.Length)
            throw new MirrorEmitException(
                "[mirror:emit] 镜像面与对标面的主构造形参数量不等："
                + $"{source.Length} vs {target.Length}（位序即语义，禁静默错位）");

        var lines = new StringBuilder();
        for (var index = 0; index < source.Length; index++)
        {
            var (sourceType, sourceInfo, sourceName) = source[index];
            var accessor = sourceVariable + "." + sourceName;
            var expression = Slot(plan, target[index].Type, sourceType, sourceInfo, accessor, fromStandard);

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
        string accessor,
        bool fromStandard)
    {
        var nullable = Nullable.GetUnderlyingType(sourceType) is not null
                       || sourceInfo.ReadState == NullabilityState.Nullable;

        if (MethodOf(plan, targetType, fromStandard) is { } nested)
            return nullable ? $"{accessor} is null ? null : {nested}({accessor})" : $"{nested}({accessor})";

        if (targetType.IsGenericType && Nullable.GetUnderlyingType(targetType) is null)
        {
            var definition = targetType.GetGenericTypeDefinition();
            var arguments = targetType.GetGenericArguments();

            if (arguments.Length == 1 && MethodOf(plan, arguments[0], fromStandard) is { } element)
                return nullable
                    ? $"{accessor}?.Select({element}).ToArray()"
                    : $"{accessor}.Select({element}).ToArray()";

            if (definition == typeof(IReadOnlyDictionary<,>)
                && MethodOf(plan, arguments[1], fromStandard) is { } value)
            {
                return nullable
                    ? $"{accessor}?.ToDictionary(kv => kv.Key, kv => {value}(kv.Value), StringComparer.Ordinal)"
                    : $"{accessor}.ToDictionary(kv => kv.Key, kv => {value}(kv.Value), StringComparer.Ordinal)";
            }
        }

        return accessor;
    }

    /// <summary>
    /// 目标槽位所属的配对方法名：<paramref name="fromStandard"/> = true → 目标类型须是计划中的**镜像**
    /// （取 <see cref="MirrorPair.ProjectMethod"/>）；否则须是计划中的**映射目标**（取 <see cref="MirrorPair.MapperMethod"/>）。
    /// </summary>
    private static string? MethodOf(MirrorPlan plan, Type targetType, bool fromStandard)
    {
        var candidate = Nullable.GetUnderlyingType(targetType) ?? targetType;

        foreach (var pair in plan.Pairs)
        {
            if (fromStandard)
            {
                if (pair.Mirror == candidate) return pair.ProjectMethod;
            }
            else if (pair.MapperTarget == candidate)
            {
                return pair.MapperMethod;
            }
        }

        return null;
    }

    /// <summary>类型渲染（<c>?</c> 恰加一次：<see cref="Nullable{T}"/> 或引用类型声明注解）。</summary>
    private static string RenderType(Type type, NullabilityInfo info, MirrorPlan plan, bool standardToMirror)
    {
        var nullable = Nullable.GetUnderlyingType(type) is not null
                       || info.ReadState == NullabilityState.Nullable;
        var core = Nullable.GetUnderlyingType(type) ?? type;
        var text = Core(core, info, plan, standardToMirror);

        return nullable ? text + "?" : text;
    }

    /// <summary>去掉可空层后的类型名（镜像侧把标准类型换名为镜像类型；集合统一为只读接口）。</summary>
    private static string Core(Type type, NullabilityInfo info, MirrorPlan plan, bool standardToMirror)
    {
        if (type.IsArray)
            return RenderType(type.GetElementType()!, info.ElementType ?? info, plan, standardToMirror) + "[]";

        if (!type.IsGenericType)
            return Name(type, plan, standardToMirror);

        var definition = type.GetGenericTypeDefinition();
        var arguments = type.GetGenericArguments();
        var infos = info.GenericTypeArguments;
        NullabilityInfo At(int index) => infos.Length == arguments.Length ? infos[index] : info;

        if (definition == typeof(IReadOnlyList<>) || definition == typeof(List<>))
            return $"IReadOnlyList<{RenderType(arguments[0], At(0), plan, standardToMirror)}>";

        if (definition == typeof(IReadOnlyDictionary<,>) || definition == typeof(Dictionary<,>))
            return $"IReadOnlyDictionary<{RenderType(arguments[0], At(0), plan, standardToMirror)}, "
                   + $"{RenderType(arguments[1], At(1), plan, standardToMirror)}>";

        throw new MirrorEmitException(
            $"[mirror:emit] 未支持的集合形态 '{type.Name}'（镜像面只认只读列表 / 只读映射）。");
    }

    /// <summary>类型名（镜像侧把标准类型换为其镜像名；<c>System</c> 类型名归一为 C# 关键字别名）。</summary>
    private static string Name(Type type, MirrorPlan plan, bool standardToMirror)
    {
        if (standardToMirror)
            foreach (var pair in plan.Pairs)
                if (pair.Standard == type)
                    return pair.Mirror.Name;

        return Keywords.GetValueOrDefault(type.Name, type.Name);
    }

    /// <summary><c>System</c> 类型名 → C# 关键字别名（生成物可读性；语义等价）。</summary>
    private static readonly Dictionary<string, string> Keywords = new(StringComparer.Ordinal)
    {
        ["Boolean"] = "bool",
        ["Byte"] = "byte",
        ["Char"] = "char",
        ["Decimal"] = "decimal",
        ["Double"] = "double",
        ["Int16"] = "short",
        ["Int32"] = "int",
        ["Int64"] = "long",
        ["Object"] = "object",
        ["SByte"] = "sbyte",
        ["Single"] = "float",
        ["String"] = "string",
        ["UInt16"] = "ushort",
        ["UInt32"] = "uint",
        ["UInt64"] = "ulong",
    };

    /// <summary>成员缩进（生成文本自带缩进，故包装类只出大括号）。</summary>
    private static string Indent(string text)
    {
        var result = new StringBuilder(text.Length);

        foreach (var line in text.Split('\n'))
            if (line.Length != 0)
                result.Append("    ").Append(line).Append('\n');

        return result.ToString();
    }

    /// <summary>默认值字面量（只认记录声明实际出现的形态；未知形态即抛，禁静默造值）。</summary>
    private static string DefaultLiteral(ParameterInfo parameter)
        => parameter.DefaultValue switch
        {
            null => "null",
            string text => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
            bool flag => flag ? "true" : "false",
            int number => number.ToString(CultureInfo.InvariantCulture),
            _ => throw new MirrorEmitException(
                $"[mirror:emit] 未支持的默认值形态：{parameter.Name}={parameter.DefaultValue}"),
        };

    /// <summary>主构造形参（位置记录：公开实例构造中形参最多者；记录副本构造非 public，故此处恰得一个）。</summary>
    private static ParameterInfo[] Positional(Type type)
        => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .First()
            .GetParameters();
}
