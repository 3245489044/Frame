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
    /// <summary>按取值法投影单个产物字段（捕获 / 计数族一律经收敛 3 代数节点求值）。</summary>
    internal static object? Field(
        string portKey, ComposedFieldStandard field, object?[] deps,
        IReadOnlyDictionary<string, object?> results, EvalScope scope, IPortInput input)
    {
        // 收敛 3：旧 select 捕获 / 计数族 → 代数节点（单一转换点 = ToCaptureExpr）；与逐档 switch 并存即漂移面
        if (field.ToCaptureExpr() is { } captureExpr)
            return ScriptedExprEvaluator.Evaluate(captureExpr, scope, input, deps, field.Field);

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

            default:
                throw new PortBindingViolationException(
                    portKey,
                    $"端口 '{portKey}' 的投影字段 '{field.Field}' 取值法 '{field.Select}' 未知" +
                    $"（{PortImplVocabulary.RenderList(PortImplVocabulary.SelectEnum)}）");
        }
    }

    /// <summary>取某次调用结果（引用必须命中已声明调用）。</summary>
    private static object? Result(string portKey, ComposedFieldStandard field, IReadOnlyDictionary<string, object?> results)
    {
        if (field.Call is not { Length: > 0 } call || !results.TryGetValue(call, out var value))
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的投影字段 '{field.Field}' 引用了未声明的调用 '{field.Call}'");

        return value;
    }

    /// <summary>属性路径取值（<c>A.B.C</c>：逐段属性优先、回落字段、忽略大小写；任一段缺失 → null）。</summary>
    private static object? PathOf(object? value, string path) => PortValues.PathOf(value, path);

    /// <summary>计数（集合 / 数组 / 字符串长度；未知载体 fail-fast，防静默投影为 0）。</summary>
    private static int Count(string portKey, object? value, string field)
        => PortValues.TryCount(value)
           ?? throw new PortBindingViolationException(
               portKey,
               $"端口 '{portKey}' 的投影字段 '{field}' 取值法 count 不适用于 {value!.GetType().Name}");
}
