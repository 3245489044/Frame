namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;
using Strategy.Observability.Diagnostics;

/// <summary>
/// 投影算子（R3-2）：组合族「产物字段 ← 取值法」的**唯一实现点**（原 <c>ComposedBinding.Project</c> 逐字迁入）。
/// <para>
/// 取值法为机制枚举（不含领域谓词）；未知取值法与缺失必填项一律 fail-fast（防静默投影为 null / 0）。
/// </para>
/// </summary>
internal static class PortProjection
{
    /// <summary>按取值法投影单个产物字段。</summary>
    internal static object? Field(
        string portKey, ComposedFieldStandard field, object?[] deps, IReadOnlyDictionary<string, object?> results)
    {
        switch (field.Select)
        {
            case "value":
                return Result(portKey, field, results);

            case "count":
                return Count(portKey, Result(portKey, field, results), field.Field);

            case "notNull":
                return Result(portKey, field, results) is not null;

            case "text":
                return PortValues.Text(Result(portKey, field, results));

            case "message":
                return (Result(portKey, field, results) as Exception)?.Message;

            case "thrown":
                return Result(portKey, field, results) is Exception;

            case "typeName":
                // G9：异常类型名（非异常 → null：「未抛出」与「抛出」两态可经 coalesce 给缺省）。
                return (Result(portKey, field, results) as Exception)?.GetType().Name;

            case "countEqual":
                if (field.Other is not { Length: > 0 } other || !results.TryGetValue(other, out var right))
                    throw new PortBindingViolationException(
                        portKey,
                        $"端口 '{portKey}' 的 countEqual 字段 '{field.Field}' 缺少可解析的比较调用（other='{field.Other}'）");

                return Count(portKey, Result(portKey, field, results), field.Field) == Count(portKey, right, field.Field);

            case "path":
                if (field.Path is not { Length: > 0 } path)
                    throw new PortBindingViolationException(
                        portKey, $"端口 '{portKey}' 的 path 字段 '{field.Field}' 缺少 path（属性路径）");

                return PathOf(Result(portKey, field, results), path);

            case "pathText":
                if (field.Path is not { Length: > 0 } textPath)
                    throw new PortBindingViolationException(
                        portKey, $"端口 '{portKey}' 的 pathText 字段 '{field.Field}' 缺少 path（属性路径）");

                return PortValues.Text(PathOf(Result(portKey, field, results), textPath));

            case "fakeCount":
                return ProxyFakes.CountOf(
                    Fake(portKey, field, deps), field.Method ?? throw Missing(portKey, field, "method"), field.Key);

            case "capturedCount":
                return Captured(portKey, field, deps).Count;

            case "capturedFirst":
                return CapturedArg(portKey, field, deps, first: true);

            case "capturedLast":
                return CapturedArg(portKey, field, deps, first: false);

            case "capturedFirstOrNull":
                return CapturedArg(portKey, field, deps, first: true, orNull: true);

            case "capturedLastOrNull":
                return CapturedArg(portKey, field, deps, first: false, orNull: true);

            default:
                throw new PortBindingViolationException(
                    portKey,
                    $"端口 '{portKey}' 的投影字段 '{field.Field}' 取值法 '{field.Select}' 未知" +
                    $"（{PortImplVocabulary.RenderList(PortImplVocabulary.SelectEnum)}）");
        }
    }

    /// <summary>依赖取替身实例（dep 必填；缺失 / 越界 / 非替身即抛，防静默取空）。</summary>
    private static object Fake(string portKey, ComposedFieldStandard field, object?[] deps)
        => field.Dep is { } index && index >= 0 && index < deps.Length && deps[index] is { } fake
            ? fake
            : throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 {field.Select} 字段 '{field.Field}' 缺少可解析的依赖（dep={field.Dep}）");

    /// <summary>捕获序（G8；<c>method</c> 给出时按成员过滤，空 = 全部成员的调用序）。</summary>
    private static IReadOnlyList<object?[]> Captured(
        string portKey, ComposedFieldStandard field, object?[] deps)
        => PortFake.CapturedOf(Fake(portKey, field, deps), field.Method);

    /// <summary>
    /// 捕获实参（G8）：首次 / 末次调用的**首个实参**；<c>path</c> 给出时取该实参的该属性路径。
    /// 无捕获记录或该调用零实参 → 默认 fail-fast（<paramref name="orNull"/> 形态则记 null，
    /// 对应「该成员未被调用」的合法空态，与脚本族 <c>firstOrNull</c>/<c>lastOrNull</c> 同惯例）。
    /// </summary>
    private static object? CapturedArg(
        string portKey, ComposedFieldStandard field, object?[] deps, bool first, bool orNull = false)
    {
        var captured = Captured(portKey, field, deps);

        if (captured.Count == 0)
            return orNull
                ? null
                : throw new PortBindingViolationException(
                    portKey,
                    $"端口 '{portKey}' 的 {field.Select} 字段 '{field.Field}' 无捕获记录（该成员未被调用）");

        var call = captured[first ? 0 : captured.Count - 1];

        if (call.Length == 0)
            return orNull
                ? null
                : throw new PortBindingViolationException(
                    portKey,
                    $"端口 '{portKey}' 的 {field.Select} 字段 '{field.Field}' 的捕获调用无实参（零参调用）");

        return field.Path is { Length: > 0 } path ? PathOf(call[0], path) : call[0];
    }

    /// <summary>取某次调用结果（引用必须命中已声明调用）。</summary>
    private static object? Result(string portKey, ComposedFieldStandard field, IReadOnlyDictionary<string, object?> results)
    {
        if (field.Call is not { Length: > 0 } call || !results.TryGetValue(call, out var value))
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的投影字段 '{field.Field}' 引用了未声明的调用 '{field.Call}'");

        return value;
    }

    private static PortBindingViolationException Missing(string portKey, ComposedFieldStandard field, string key)
        => new(portKey, $"端口 '{portKey}' 的字段 '{field.Field}' 取值法 '{field.Select}' 缺少 {key}");

    /// <summary>属性路径取值（<c>A.B.C</c>：逐段属性优先、回落字段、忽略大小写；任一段缺失 → null）。</summary>
    private static object? PathOf(object? value, string path) => PortValues.PathOf(value, path);

    /// <summary>计数（集合 / 数组 / 字符串长度；未知载体 fail-fast，防静默投影为 0）。</summary>
    private static int Count(string portKey, object? value, string field)
        => PortValues.TryCount(value)
           ?? throw new PortBindingViolationException(
               portKey,
               $"端口 '{portKey}' 的投影字段 '{field}' 取值法 count 不适用于 {value!.GetType().Name}");
}
