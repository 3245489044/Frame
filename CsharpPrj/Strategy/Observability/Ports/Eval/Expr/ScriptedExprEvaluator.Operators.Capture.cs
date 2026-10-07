namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;
using Strategy.Observability.Diagnostics;

/// <summary>
/// 替身捕获 / 计数算子（收敛 3 代数节点）：<c>capture</c> / <c>captureCount</c> / <c>fakeCount</c>。
/// <para>
/// 单源：底层取值一律经 <see cref="PortFake.CapturedOf"/> / <see cref="ProxyFakes.CountOf"/>，
/// 本件只负责「依赖下标 → 替身实例」的解析与空态口径（无捕获 / 零参 / 越界）。
/// </para>
/// </summary>
internal static partial class ScriptedExprEvaluator
{
    /// <summary>
    /// 依赖取替身实例（<paramref name="dep"/> 必填；缺失 / 越界 / 非替身即抛，防静默取空）。
    /// <para>组合族（<c>PortProjection</c>）与代数节点共用本件，禁各处手写等价解析。</para>
    /// </summary>
    internal static object ResolveFake(string portKey, string node, string field, int? dep, object?[] deps)
        => dep is { } index && index >= 0 && index < deps.Length && deps[index] is { } fake
            ? fake
            : throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 {node} 字段 '{field}' 缺少可解析的依赖（dep={dep}）");

    /// <summary>
    /// 捕获取值（<c>capture</c>）：取首次 / 末次调用的第 <c>index</c> 个实参（可带属性路径）。
    /// 无捕获记录 / 零参调用 / 越界 → 默认 fail-fast（<c>orNull</c> 形态记 null）。
    /// </summary>
    private static object? Capture(
        CaptureExprStandard capture, EvalScope scope, object?[] deps, string field)
    {
        var fake = ResolveFake(scope.PortKey, "capture", field, capture.Dep, deps);
        var captured = PortFake.CapturedOf(fake, capture.Member);

        if (captured.Count == 0)
            return capture.OrNull
                ? null
                : throw new PortBindingViolationException(
                    scope.PortKey,
                    $"端口 '{scope.PortKey}' 的 capture 字段 '{field}' 无捕获记录（该成员未被调用）");

        var call = captured[string.Equals(capture.At, "last", StringComparison.Ordinal) ? captured.Count - 1 : 0];

        if (call.Length == 0)
            return capture.OrNull
                ? null
                : throw new PortBindingViolationException(
                    scope.PortKey,
                    $"端口 '{scope.PortKey}' 的 capture 字段 '{field}' 的捕获调用无实参（零参调用）");

        var index = capture.Index ?? 0;

        if (index < 0 || index >= call.Length)
            return capture.OrNull
                ? null
                : throw new PortBindingViolationException(
                    scope.PortKey,
                    $"端口 '{scope.PortKey}' 的 capture 字段 '{field}' 实参下标 {index} 越界"
                    + $"（该调用 {call.Length} 个实参）");

        return capture.Path is { Length: > 0 } path ? PortValues.PathOf(call[index], path) : call[index];
    }

    /// <summary>捕获计数（<c>captureCount</c>）：该替身（可经 <c>member</c> 过滤）的调用序长度。</summary>
    private static int CaptureCount(
        CaptureCountExprStandard node, EvalScope scope, object?[] deps, string field)
        => PortFake.CapturedOf(ResolveFake(scope.PortKey, "captureCount", field, node.Dep, deps), node.Member).Count;

    /// <summary>调用计数（<c>fakeCount</c>）：按成员名（可带主键）计数；成员名必填。</summary>
    private static int FakeCount(FakeCountExprStandard node, EvalScope scope, object?[] deps, string field)
        => ProxyFakes.CountOf(
            ResolveFake(scope.PortKey, "fakeCount", field, node.Dep, deps),
            node.Member ?? throw new PortBindingViolationException(
                scope.PortKey,
                $"端口 '{scope.PortKey}' 的 fakeCount 字段 '{field}' 缺少 member（被计数成员名）"),
            node.Key);
}