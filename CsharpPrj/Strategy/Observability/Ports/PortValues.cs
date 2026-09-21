using System.Reflection;
using System.Text.Json;

namespace Strategy.Observability.Ports;

/// <summary>
/// 取值小件单源（组合绑定 / 脚本绑定 / 实参构造共用）：属性路径取值、计数、JSON 归一、文本形式。
/// <para>分工：只做纯值变换，不做任何诊断抛错（不可计数 → 返回 null，由调用方按其上下文给出可定位异常）。</para>
/// </summary>
internal static class PortValues
{
    /// <summary>属性路径取值（<c>A.B.C</c>：逐段属性优先、回落字段、忽略大小写；任一段缺失 → null）。</summary>
    internal static object? PathOf(object? value, string path)
    {
        var current = value;
        foreach (var raw in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is null) return null;

            var (segment, index) = Segment(raw);
            if (segment is { Length: > 0 }) current = MemberOf(current, segment);
            if (index is { } position) current = ElementAt(current, position);
        }

        return current;
    }

    private static (string? Segment, int? Index) Segment(string raw)
    {
        var open = raw.IndexOf('[', StringComparison.Ordinal);
        if (open < 0 || !raw.EndsWith(']')) return (raw, null);

        var digits = raw[(open + 1)..^1];
        return int.TryParse(digits, out var index) ? (raw[..open], index) : (raw, null);
    }

    private static object? MemberOf(object value, string segment) => value switch
    {
        JsonElement element when element.ValueKind == JsonValueKind.Object
            => JsonMember(element, segment),
        IReadOnlyDictionary<string, object?> readOnly
            => readOnly.TryGetValue(segment, out var item) ? item : null,
        System.Collections.IDictionary dictionary
            => dictionary.Contains(segment) ? dictionary[segment] : null,
        _ when GenericDictionary(value) is { } generic
            => GenericMember(generic, value, segment),
        _ => ReflectedMember(value, segment),
    };

    /// <summary>
    /// 泛型字典接口视图（键类型为 <c>string</c> / <c>object</c> 的 <c>IDictionary&lt;,&gt;</c> /
    /// <c>IReadOnlyDictionary&lt;,&gt;</c>）。
    /// <para>
    /// 为什么需要（静默错值防线）：只实现泛型字典接口而**不**实现非泛型 <c>IDictionary</c> 的容器
    /// （如 <c>DataSpace : IDictionary&lt;string,object&gt;</c>），落到 <see cref="ReflectedMember"/> 会把
    /// <c>path Data.count</c> 解析成 <c>Count</c> **属性**（得条目数而非键值，且不报错）。
    /// 本视图使其按**键**取值优先，缺失即 null（⊥，可见），不静默取反射成员。
    /// </para>
    /// </summary>
    private static Type? GenericDictionary(object value)
        => value.GetType().GetInterfaces().FirstOrDefault(candidate =>
            candidate.IsGenericType
            && (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                || candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
            && candidate.GetGenericArguments()[0] is var key
            && (key == typeof(string) || key == typeof(object)));

    private static object? GenericMember(Type view, object value, string segment)
    {
        var tryGet = view.GetMethod("TryGetValue");
        if (tryGet is null) return null;

        var arguments = new object?[] { segment, null };
        return tryGet.Invoke(value, arguments) is true ? arguments[1] : null;
    }

    private static object? JsonMember(JsonElement element, string segment)
    {
        if (element.TryGetProperty(segment, out var direct)) return direct.Clone();

        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, segment, StringComparison.OrdinalIgnoreCase))
                return property.Value.Clone();

        return null;
    }

    private static object? ReflectedMember(object current, string segment)
    {
        var type = current.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        var member = (MemberInfo?)type.GetProperty(segment, flags)
                     ?? type.GetProperties(flags).FirstOrDefault(p =>
                         string.Equals(p.Name, segment, StringComparison.OrdinalIgnoreCase))
                     ?? type.GetField(segment, flags)
                     ?? (MemberInfo?)type.GetFields(flags).FirstOrDefault(f =>
                         string.Equals(f.Name, segment, StringComparison.OrdinalIgnoreCase));

        return member is null ? null : ReflectivePortResolver.ReadMember(member, current);
    }

    private static object? ElementAt(object? value, int position)
        => TryElementAt(value, position, out var indexed)
            ? indexed
            : Elements(value).ElementAtOrDefault(position);

    /// <summary>可索引载体判定（JSON 数组 / JSON 字符串 / 字符串 / <c>IList</c>）：用于区分「越界」与「不可索引」。</summary>
    internal static bool IsIndexable(object? value)
        => value switch
        {
            JsonElement element => element.ValueKind is JsonValueKind.Array or JsonValueKind.String,
            string => true,
            System.Collections.IList => true,
            _ => false,
        };

    /// <summary>
    /// 索引取值的**唯一实现**（JSON 数组 / JSON 字符串 / 字符串 / <c>IList</c>；越界 → false）。
    /// 调用方按各自口径处理不可索引载体（属性路径取值取 null；脚本遍历对非序载体 fail-fast）。
    /// </summary>
    internal static bool TryElementAt(object? value, int position, out object? result)
    {
        result = null;
        if (position < 0) return false;

        switch (value)
        {
            case JsonElement element when element.ValueKind == JsonValueKind.Array:
                if (element.GetArrayLength() <= position) return false;
                result = element[position].Clone();
                return true;

            case JsonElement element when element.ValueKind == JsonValueKind.String:
                var json = element.GetString() ?? string.Empty;
                if (position >= json.Length) return false;
                result = json[position].ToString();
                return true;

            case string text:
                if (position >= text.Length) return false;
                result = text[position].ToString();
                return true;

            case System.Collections.IList list:
                if (position >= list.Count) return false;
                result = list[position];
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 计数（集合 / 数组 / 字符串长度 / JSON 数组元素数 / JSON 对象属性数；空 → 0；不可计数 → null）。
    /// <para>JSON 结构计入本口径（E7：<c>JsonElement</c> 不实现 <c>IEnumerable</c>，缺此分支则集合元素聚合走不进计数回退）。</para>
    /// </summary>
    internal static int? TryCount(object? value)
        => value switch
        {
            null => 0,
            string text => text.Length,
            JsonElement element => element.ValueKind switch
            {
                JsonValueKind.Array => element.GetArrayLength(),
                JsonValueKind.Object => element.EnumerateObject().Count(),
                JsonValueKind.Null or JsonValueKind.Undefined => 0,
                _ => null,
            },
            System.Collections.ICollection collection => collection.Count,
            System.Collections.IEnumerable sequence => CountByIteration(sequence),
            _ => null,
        };

    /// <summary>文本形式（null → null；JSON 元素按值语义取文本：字符串取值、结构取原文；其余 <c>ToString</c>）。</summary>
    internal static string? Text(object? value) => value switch
    {
        null => null,
        JsonElement element => element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : element.GetRawText(),
        _ => value.ToString(),
    };

    /// <summary>
    /// 不变量文本化（null → null；bool → True/False；数值不变量；JSON 按值语义）——<b>出证文本唯一单源</b>：
    /// 调用记录（<see cref="PortCallView.Text"/>）与观测产物（<c>ObservationOutcome.TextOf</c>）同调本件，禁各写一份 switch。
    /// </summary>
    internal static string? TextInvariant(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "True" : "False",
        JsonElement element => element.ValueKind == JsonValueKind.Null ? null : element.ToString(),
        double real => real.ToString(System.Globalization.CultureInfo.InvariantCulture),
        float real => real.ToString(System.Globalization.CultureInfo.InvariantCulture),
        decimal real => real.ToString(System.Globalization.CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>条件真值（null / false / 空串 / 空 JSON / 零数 → 假；其余 → 真）。</summary>
    internal static bool Truthy(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        string text => text.Length > 0 && !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase),
        JsonElement element => element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False or JsonValueKind.Null => false,
            JsonValueKind.String => (element.GetString() ?? string.Empty).Length > 0,
            JsonValueKind.Number => element.GetDouble() != 0,
            JsonValueKind.Array => element.GetArrayLength() > 0,
            JsonValueKind.Object => element.EnumerateObject().Any(),
            _ => false,
        },
        _ => true,
    };

    /// <summary>数值形式（数值 / 数值文本 → <c>decimal</c>；其余 → null）。</summary>
    internal static decimal? Number(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case decimal decimalValue:
                return decimalValue;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double:
                return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
            case JsonElement element when element.ValueKind == JsonValueKind.Number:
                return element.TryGetDecimal(out var parsed) ? parsed : null;
            case string text:
                return decimal.TryParse(
                    text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number)
                    ? number
                    : null;
            default:
                return null;
        }
    }

    /// <summary>JSON 值 → 原义值（标量取 CLR 值；null / undefined → null；结构保留元素）。</summary>
    internal static object? Unwrap(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
            _ => element.Clone(),
        };

    /// <summary>
    /// 原义解包（E9：对取值调用前的解包）：JSON **标量** → CLR 值；JSON **数组 / 对象保持 <c>JsonElement</c>**
    /// （口径写死：与 <see cref="Elements"/> / <see cref="PathOf"/> 的既有分派一致，避免类型漂移）。
    /// </summary>
    internal static object? UnwrapValue(object? value) => value is JsonElement element ? Unwrap(element) : value;

    /// <summary>
    /// 值归一（**单一口径**，E4；<c>Direct</c> / <c>Coerce</c> / 数组元素共用）：源值 → 目标类型的可比转换。
    /// <para>口径：JSON 数字逐级收窄（越界 / 有小数位落整型 → 不可归一，不静默截断）；JSON 字符串 → 值类型 / 枚举（宽松解析，InvariantCulture）；
    /// JSON <c>true/false</c> → <c>bool</c> 直投；CLR 数值 → 目标数值 checked 转换；非文本值不隐式文本化。</para>
    /// </summary>
    internal static bool TryNormalize(object? value, Type target, out object? result)
    {
        result = null;
        if (value is null) return false;

        var underlying = Nullable.GetUnderlyingType(target) ?? target;
        if (underlying == typeof(object) || underlying.IsInstanceOfType(value))
        {
            result = value;
            return true;
        }

        if (value is JsonElement element) return TryNormalizeJson(element, target, underlying, out result);

        if (underlying.IsEnum)
        {
            if (value is string name) return TryParseEnum(underlying, name, out result);
            if (!IsNumeric(value)) return false;

            try
            {
                result = Enum.ToObject(underlying, value);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidCastException)
            {
                result = null;
                return false;
            }
        }

        if (IsNumeric(underlying))
        {
            if (value is string text) return TryParseNumeric(text, underlying, out result);
            if (!IsNumeric(value)) return false;
            if (IsIntegral(underlying) && HasFraction(value)) return false;

            try
            {
                result = System.Convert.ChangeType(value, underlying, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is OverflowException or FormatException or InvalidCastException)
            {
                result = null;
                return false;
            }
        }

        if (underlying == typeof(bool))
        {
            switch (value)
            {
                case bool flag:
                    result = flag;
                    return true;
                case string literal when bool.TryParse(literal, out var parsed):
                    result = parsed;
                    return true;
                case string digits when digits is "1" or "0":
                    result = digits == "1";
                    return true;
                default:
                    return false;
            }
        }

        if (underlying == typeof(char) && value is string single && single.Length == 1)
        {
            result = single[0];
            return true;
        }

        if (value is not string text2) return false;

        try
        {
            result = System.Convert.ChangeType(text2, underlying, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            result = null;
            return false;
        }
    }

    /// <summary>
    /// 标量文本转换单源（reflective 位置实参 / 组合实参 / 脚本取值三处共用）：<c>bool</c>（True/False + 1/0）、
    /// <c>int</c>、<c>double</c>（InvariantCulture）。只做解析，失败由各调用方按其自有诊断文案报错（文案不收敛）。
    /// <para><paramref name="expected"/>：命中已知标量转换时给出该转换的期望文案（布尔/整数/数值），供调用方拼自有诊断；
    /// 非标量转换为空串（调用方继续其 string/csv/json/未知分派）。</para>
    /// </summary>
    internal static bool TryConvertScalar(string convert, string text, out object? value, out string expected)
    {
        switch (convert)
        {
            case "bool":
                expected = "布尔";
                if (TryNormalize(text, typeof(bool), out var parsed) && parsed is bool flag)
                {
                    value = flag;
                    return true;
                }
                break;

            case "int":
                expected = "整数";
                if (int.TryParse(text, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var number))
                {
                    value = number;
                    return true;
                }
                break;

            case "double":
                expected = "数值";
                if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var real))
                {
                    value = real;
                    return true;
                }
                break;

            default:
                expected = string.Empty;
                break;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// 数值序列统计单源（聚合投影 <c>AggregateProjection</c> 与脚本 <c>agg</c> 算子共用）：单遍累加
    /// count/sum/max/min。空集：Count=0、Sum=0、Max/Min=null（均值空集口径由调用方自定：投影返 0，脚本 fail-fast）。
    /// 极值哨兵沿用 double.MinValue/MaxValue 实现（保持既有 NaN 比较行为逐字不变）。
    /// </summary>
    internal readonly struct NumericStats
    {
        private NumericStats(long count, double sum, double? max, double? min)
        {
            Count = count;
            Sum = sum;
            Max = max;
            Min = min;
        }

        /// <summary>参与统计的样本数。</summary>
        public long Count { get; }

        /// <summary>总和（空集 = 0）。</summary>
        public double Sum { get; }

        /// <summary>最大值（空集 = null）。</summary>
        public double? Max { get; }

        /// <summary>最小值（空集 = null）。</summary>
        public double? Min { get; }

        /// <summary>均值（空集 = 0；调用方需 fail-fast 时先判 <see cref="Count"/>）。</summary>
        public double Mean => Count == 0 ? 0 : Sum / Count;

        /// <summary>单遍累加（求和序 = 枚举序，与 LINQ Sum 同序）。</summary>
        public static NumericStats Over(IEnumerable<double> values)
        {
            long count = 0;
            double sum = 0;
            var max = double.MinValue;
            var min = double.MaxValue;

            foreach (var value in values)
            {
                count++;
                sum += value;
                if (value > max) max = value;
                if (value < min) min = value;
            }

            return new NumericStats(count, sum, count == 0 ? null : max, count == 0 ? null : min);
        }
    }

    /// <summary>JSON 值 → 目标类型（null / undefined 归调用方裁决；标量走 <see cref="TryNormalize"/>，结构回落 STJ）。</summary>
    private static bool TryNormalizeJson(JsonElement element, Type target, Type underlying, out object? result)
    {
        result = null;

        switch (element.ValueKind)
        {
            case JsonValueKind.Null or JsonValueKind.Undefined:
                return false;

            case JsonValueKind.True or JsonValueKind.False:
                return TryNormalize(element.GetBoolean(), target, out result);

            case JsonValueKind.String:
                return TryNormalize(element.GetString(), target, out result);

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer)) return TryNormalize(integer, target, out result);
                if (element.TryGetDecimal(out var dec) && TryNormalize(dec, target, out result)) return true;

                return TryNormalize(element.GetDouble(), target, out result);
        }

        if (target == typeof(JsonElement) || underlying == typeof(object))
        {
            result = element.Clone();
            return true;
        }

        try
        {
            result = JsonSerializer.Deserialize(element.GetRawText(), target);
            return true;
        }
        catch (JsonException)
        {
            result = null;
            return false;
        }
        catch (NotSupportedException)
        {
            result = null;
            return false;
        }
    }

    /// <summary>
    /// 可绑定度等级（E1 重载解析 / E9 方法解析共用评分：「精确类型命中」&gt;「可归一」&gt;「可选形参缺省」&gt;「不可绑定」）。
    /// </summary>
    internal enum Bindability
    {
        Unbindable = -1,
        Defaulted = 0,
        Normalizable = 1,
        Exact = 2,
    }

    /// <summary>实参对目标形参的可绑定度（null 仅对可空 / 引用类型为精确命中）。</summary>
    internal static Bindability Score(object? value, Type target)
    {
        if (value is null)
            return target.IsValueType && Nullable.GetUnderlyingType(target) is null
                ? Bindability.Unbindable
                : Bindability.Exact;

        var underlying = Nullable.GetUnderlyingType(target) ?? target;
        if (target.IsInstanceOfType(value) || underlying.IsInstanceOfType(value)) return Bindability.Exact;

        return TryNormalize(value, target, out _) ? Bindability.Normalizable : Bindability.Unbindable;
    }

    /// <summary>候选分数：未给出的形参位按「可选形参缺省」计；任一不可绑定实参 → 负分（即「不可绑定」）。</summary>
    internal static int ScoreArguments(Type[] parameters, object?[] args)
    {
        var total = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            var score = i < args.Length ? Score(args[i], parameters[i]) : Bindability.Defaulted;
            if (score == Bindability.Unbindable) return -1;

            total += (int)score;
        }

        return total;
    }

    private static bool TryParseEnum(Type type, string name, out object? result)
    {
        try
        {
            result = Enum.Parse(type, name, ignoreCase: true);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            result = null;
            return false;
        }
    }

    private static bool TryParseNumeric(string text, Type target, out object? result)
    {
        try
        {
            result = System.Convert.ChangeType(text, target, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            result = null;
            return false;
        }
    }

    private static bool IsNumeric(object value)
        => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static bool IsNumeric(Type type)
        => type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort)
           || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
           || type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static bool IsIntegral(Type type)
        => type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort)
           || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);

    private static bool HasFraction(object value)
        => value switch
        {
            float real => real % 1 != 0,
            double real => real % 1 != 0,
            decimal real => real % 1 != 0,
            _ => false,
        };

    /// <summary>JSON 值 → 目标类型（<c>JsonElement</c>/<c>object</c> 位保留元素；null / undefined → null；标量按归一口径）。</summary>
    internal static object? Coerce(object? value, Type target)
    {
        if (value is JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;

            return TryNormalize(element, target, out var normalized)
                ? normalized
                : JsonSerializer.Deserialize(element.GetRawText(), target);
        }

        return TryNormalize(value, target, out var result) ? result : value;
    }

    /// <summary>
    /// 位置实参**对象直投**判定（对象实参通道单源）：<c>convert</c> 显式为 <c>object</c>/<c>passthrough</c>；
    /// 或 <c>convert</c> 缺省且实参本身**非文本载体**（非 <c>string</c> / 非 <c>JsonElement</c>）——
    /// 后者为调用方直传对象的形态（如产品组合根注入的实例），不得被 <c>ToString</c> 文本化。
    /// <para>null 实参不属直投（缺参语义仍由调用方按 <c>optional</c>/<c>default</c> 裁决）。</para>
    /// </summary>
    internal static bool IsDirectPositional(string? convert, object? raw)
        => raw is not null
           && (convert is "object" or "passthrough"
               || (convert is null or "" && raw is not (string or JsonElement)));

    /// <summary>直投值的目标类型兼容性（<c>object</c> 位恒兼容；null 由调用方先行裁决）。</summary>
    internal static bool FitsDirect(object? value, Type target)
        => target == typeof(object) || value is null || target.IsInstanceOfType(value);

    /// <summary>序列展开为元素集（非序载体 → 单元素；null → 空集，与夹具 <c>?? Array.Empty&lt;T&gt;()</c> 口径一致）。</summary>
    internal static IEnumerable<object?> Elements(object? value)
    {
        switch (value)
        {
            case null:
                return Array.Empty<object?>();
            case string text:
                return text.Select(c => (object?)c.ToString());
            case JsonElement element when element.ValueKind == JsonValueKind.Array:
                return element.EnumerateArray().Select(item => (object?)item.Clone()).ToArray();
            case JsonElement scalar:
                return new[] { (object?)scalar.Clone() };
            case System.Collections.IEnumerable sequence:
                return sequence.Cast<object?>();
            default:
                return new[] { value };
        }
    }

    /// <summary>对象属性 / 字典逐项展开（供 <c>spread</c> 动态键写入产物）。</summary>
    internal static IReadOnlyDictionary<string, object?> Spread(object? value)
    {
        var entries = new Dictionary<string, object?>(StringComparer.Ordinal);

        switch (value)
        {
            case null:
                return entries;

            case IReadOnlyDictionary<string, object?> readOnly:
                foreach (var (key, item) in readOnly) entries[key] = item;
                return entries;

            case System.Collections.IDictionary dictionary:
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                    if (entry.Key is not null) entries[entry.Key.ToString()!] = entry.Value;
                return entries;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        foreach (var property in value.GetType().GetProperties(flags))
            entries[property.Name] = property.GetValue(value);

        return entries;
    }

    private static int CountByIteration(System.Collections.IEnumerable sequence)
    {
        var count = 0;
        foreach (var _ in sequence) count++;
        return count;
    }

    /// <summary>
    /// JSON → 通用对象图（数组 → <c>List&lt;object?&gt;</c>、对象 → <c>Dictionary&lt;string,object?&gt;</c>、标量 → CLR 值）。
    /// <para>用途：声明面以 <c>convert: graph</c> 取值时无需写出 CLR 类型全名即可遍历 / 取下标 / 取属性。</para>
    /// </summary>
    internal static object? ToGraph(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray().Select(ToGraph).ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(property => property.Name, property => ToGraph(property.Value), StringComparer.Ordinal),
            _ => Unwrap(element),
        };

    /// <summary>
    /// CSV 文本切分（去空段 + 逐项去空白）——<c>convert: csv</c> 在三条绑定通道上的**唯一切分实现**
    /// （口径与 DataDriven 侧 <c>SpecLiterals.SplitCsv</c> 一致）。
    /// </summary>
    internal static string[] SplitCsv(string text)
        => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
