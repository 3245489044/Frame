using Share.CircuitFramework.Compiler;

namespace Share.CircuitFramework.Execution;

/// <summary>
/// 数据流符号的取值执行（B3-1）：把编译期派生的一条 <see cref="InputBinding"/> 解析为端口值。
/// 纯函数、零领域类型；取值只在上下文键上查表，**不引入第二条数据路径**。
/// <para>
/// 缺值语义与既有 Wire/Entry 一致：上下文里取不到 ⇒ 返回 false ⇒ 该端口本次不做赋值（不伪造 null）；
/// 例外是 <see cref="InputBindingKind.Literal"/>（常量恒有值）与 <see cref="InputBindingKind.Fold"/>（无源即空数组）。
/// </para>
/// </summary>
internal static class DataflowValueResolver
{
    /// <summary>解析一条绑定；false = 该端口本次无值（跳过赋值）。</summary>
    internal static bool TryResolve(
        IDictionary<string, object?> data, InputBinding binding, out object? value)
    {
        switch (binding.Kind)
        {
            case InputBindingKind.Literal:
                value = binding.Payload;
                return true;

            case InputBindingKind.Fold:
                {
                    var keys = binding.SourceKeys ?? Array.Empty<string>();
                    var values = new List<object?>(keys.Count);
                    foreach (var key in keys)
                        if (data.TryGetValue(key, out var item)) values.Add(item);
                    value = values;
                    return true;
                }

            case InputBindingKind.Projection:
            case InputBindingKind.Unfold:
                {
                    if (!data.TryGetValue(binding.SourceKey, out var source))
                    {
                        value = null;
                        return false;
                    }
                    return TryExtract(source, binding.Payload as string, out value);
                }

            default: // Wire / Entry / Param：上下文键直取
                return data.TryGetValue(binding.SourceKey, out value);
        }
    }

    /// <summary>
    /// 按字段路径取值（点链；支持字典键与列表下标）。路径为空 = 取原值；
    /// 路径不可达 ⇒ false（不伪造 null，与「缺失即跳过」同口径）。
    /// </summary>
    internal static bool TryExtract(object? source, string? fieldPath, out object? value)
    {
        value = source;
        if (string.IsNullOrEmpty(fieldPath)) return true;

        foreach (var segment in fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryStep(value, segment, out value))
            {
                value = null;
                return false;
            }
        }
        return true;
    }

    private static bool TryStep(object? current, string segment, out object? next)
    {
        if (current is IReadOnlyDictionary<string, object?> readOnly && readOnly.TryGetValue(segment, out next))
            return true;
        if (current is IDictionary<string, object?> mutable && mutable.TryGetValue(segment, out next))
            return true;
        if (current is System.Collections.IList list
            && int.TryParse(segment, out var index) && index >= 0 && index < list.Count)
        {
            next = list[index];
            return true;
        }

        next = null;
        return false;
    }
}
