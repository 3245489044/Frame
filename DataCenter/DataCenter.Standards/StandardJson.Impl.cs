using System.Text.Json;
using DataCenter.Protocol.Standards;
using static Plate.JsonMethod.Utilities.JsonTolerantReader;

namespace DataCenter.Standards;

/// <summary>
/// <c>impl</c> 段读取（B3 自内核 <c>PortLedger</c> 迁入）：板块外层（kind / id / target / method / reason /
/// legacyKind / productFields）+ 反射族实参 + 组合族（deps / calls / project）读取。
/// <para>
/// 边界（防两处各判一遍）：本件只做「形状读取 + 缺失必填即抛」（诊断前缀 <c>[port:standard]</c> 与内核逐字一致）；
/// **未知键闭合与词表枚举**由结构门（<c>schemas\ports.schema.json</c>）单一承担，
/// **跨项一致性**由语义判据（C7–C13）承担，本件不重复表达。
/// </para>
/// </summary>
public static partial class StandardJson
{
    /// <summary>
    /// 单条端口的 <c>impl</c> → 标准模型（B3 主线）：<c>impl</c> 缺省或为简写字符串时取 kind，
    /// 其余声明按 kind 分派读取（与内核 <c>PortLedger.Read</c> 同口径）。
    /// </summary>
    internal static PortImplStandard ReadImpl(string portKey, JsonElement spec)
    {
        if (!spec.TryGetProperty("impl", out var impl) || impl.ValueKind != JsonValueKind.Object)
        {
            var text = impl.ValueKind == JsonValueKind.String ? impl.GetString() : null;
            return new PortImplStandard(NormalizeKind(portKey, text));
        }

        var kind = NormalizeKind(portKey, Str(impl, "kind"));
        EnsureLayerKeys(nameof(PortImplVocabulary.ImplKeys), $"ports.{portKey}.impl", impl);
        var scripted = string.Equals(kind, PortImplVocabulary.KindName.Scripted, StringComparison.Ordinal);

        return new PortImplStandard(
            kind,
            Str(impl, "id"),
            Str(impl, "target"),
            Str(impl, "method"),
            Str(impl, "reason"),
            Str(impl, "legacyKind"),
            Str(impl, "productFields") ?? string.Empty,
            ReadArgs(portKey, impl),
            scripted ? ReadBinds(portKey, impl) : null,
            ReadDeps(portKey, impl),
            string.Equals(kind, PortImplVocabulary.KindName.Composed, StringComparison.Ordinal) ? ReadCalls(portKey, impl) : null,
            string.Equals(kind, PortImplVocabulary.KindName.Composed, StringComparison.Ordinal) ? ReadProject(portKey, impl) : null,
            scripted ? ReadScript(portKey, impl) : null,
            scripted ? ReadScriptProject(portKey, impl) : null,
            scripted ? ReadDeliver(portKey, impl) : null,
            scripted ? ReadScopes(portKey, impl) : null,
            scripted ? ReadScenarios(portKey, impl) : null);
    }

    /// <summary>
    /// 种类归一（SK-28：**缺省已取消** —— <c>impl.kind</c> 必填，缺即抛；非法即抛）。
    /// 取值域 = 词表唯一源（<c>Kinds</c>，禁手抄 kind）；<c>delegate</c> 属遗留值（只读兼容，新增禁止）。
    /// </summary>
    private static string NormalizeKind(string portKey, string? text)
        => text is null or ""
            ? throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 impl.kind 必填（缺省已取消：不得再隐式按 delegate 处理；"
                + $"允许值：{PortImplVocabulary.RenderList(PortImplVocabulary.Kinds)}，"
                + "其中 delegate 为遗留值（新增禁止））")
            : PortImplVocabulary.IsKnownKind(text)
                ? text
                : throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的 impl.kind '{text}' 非法（允许值："
                    + $"{PortImplVocabulary.RenderList(PortImplVocabulary.Kinds)}，其中 delegate 为遗留值（新增禁止））");

    /// <summary>
    /// 反射族位置实参读取（<c>impl.args[]</c>）：每项 <c>{ convert, optional?, nullLiteral?, default?, from?, key? }</c>；
    /// 缺 <c>convert</c> 即抛（防取值口径静默漂移）。
    /// </summary>
    private static IReadOnlyList<PortArgStandard> ReadArgs(string portKey, JsonElement impl)
    {
        if (!impl.TryGetProperty("args", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return [];

        var args = new List<PortArgStandard>();
        var index = -1;
        foreach (var item in declared.EnumerateArray())
        {
            index++;
            EnsureLayerKeys(nameof(PortImplVocabulary.ArgKeys), $"ports.{portKey}.impl.args[{index}]", item);

            var convert = Str(item, "convert")
                ?? throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的 impl.args 项缺少 convert");

            args.Add(new PortArgStandard(
                convert,
                Bool(item, "optional") ?? false,
                Bool(item, "nullLiteral") ?? false,
                item.TryGetProperty("default", out var fallback) ? fallback.Clone() : null,
                Str(item, "from"),
                Str(item, "key")));
        }

        return args;
    }

    /// <summary>
    /// 依赖声明读取（<c>impl.deps[]</c> / 场景主体 <c>deps[]</c>）：
    /// <c>new</c> / <c>factory</c> / <c>fake</c> 三形态之一，其余字段按形态取用；未声明形态即抛。
    /// </summary>
    internal static IReadOnlyList<DepStandard> ReadDeps(string portKey, JsonElement impl)
    {
        if (!impl.TryGetProperty("deps", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return [];

        var deps = new List<DepStandard>();
        var index = -1;

        foreach (var item in declared.EnumerateArray())
        {
            index++;
            EnsureLayerKeys(nameof(PortImplVocabulary.DepKeys), $"ports.{portKey}.impl.deps[{index}]", item);

            var spec = new DepStandard(
                Str(item, "new"),
                Str(item, "factory"),
                Str(item, "fake"),
                Str(item, "contract"),
                Int(item, "wrap"),
                StrList(item, "methods"),
                Elements(item, "values"),
                StrList(item, "genericArgs"),
                ReadComposedArgs(portKey, item),
                ReadFakeCalls(portKey, item, index),
                ReadFakeCapture(portKey, item, index));

            if (spec.New is null && spec.Factory is null && spec.Fake is null)
                throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的 impl.deps[{index}] 未声明形态（须给 new / factory / fake）");

            deps.Add(spec);
        }

        return deps;
    }

    /// <summary>
    /// 替身应答脚本读取（<c>deps[].calls[]</c>；G1）：每项 <c>{ member?, position?, value?, throw?, message? }</c>；
    /// <c>value</c> 与 <c>throw</c> 二选一（皆缺 = 应答 null），两皆给出即抛（防声明歧义静默择一）。
    /// </summary>
    private static IReadOnlyList<FakeCallStandard>? ReadFakeCalls(string portKey, JsonElement dep, int depIndex)
    {
        if (!dep.TryGetProperty("calls", out var declared)) return null;

        if (declared.ValueKind != JsonValueKind.Array)
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 impl.deps[{depIndex}].calls 须为数组");

        var calls = new List<FakeCallStandard>();
        var index = -1;

        foreach (var item in declared.EnumerateArray())
        {
            index++;
            EnsureLayerKeys(
                nameof(PortImplVocabulary.FakeCallKeys),
                $"ports.{portKey}.impl.deps[{depIndex}].calls[{index}]", item);

            var hasValue = item.TryGetProperty("value", out var value);
            var throwType = Str(item, "throw");

            if (hasValue && throwType is not null)
                throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的 impl.deps[{depIndex}].calls[{index}] 同时声明 value 与 throw" +
                    "（应答与抛出二选一）");

            calls.Add(new FakeCallStandard(
                Str(item, "member"),
                Int(item, "position"),
                hasValue ? value : null,
                hasValue,
                throwType,
                Str(item, "message")));
        }

        return calls.Count > 0 ? calls : null;
    }

    /// <summary>
    /// 替身调用捕获读取（<c>deps[].capture</c>；G8）：<c>{ member? }</c>；非对象即抛（防声明残缺静默降级）。
    /// </summary>
    private static FakeCaptureStandard? ReadFakeCapture(string portKey, JsonElement dep, int depIndex)
    {
        if (!dep.TryGetProperty("capture", out var declared)) return null;

        if (declared.ValueKind != JsonValueKind.Object)
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 impl.deps[{depIndex}].capture 须为对象");

        EnsureLayerKeys(
            nameof(PortImplVocabulary.FakeCaptureKeys),
            $"ports.{portKey}.impl.deps[{depIndex}].capture", declared);

        return new FakeCaptureStandard(Str(declared, "member"));
    }

    /// <summary>
    /// 组合族调用声明读取（<c>impl.calls[]</c>）：每项 <c>{ as, target?, method, property?, dep?, await?, expect?, args? }</c>；
    /// 缺 <c>as</c>/<c>method</c> 即抛（防声明残缺静默绑定）。
    /// </summary>
    private static IReadOnlyList<CallStandard> ReadCalls(string portKey, JsonElement impl)
    {
        if (!impl.TryGetProperty("calls", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return [];

        var calls = new List<CallStandard>();
        var index = -1;
        foreach (var item in declared.EnumerateArray())
        {
            index++;
            EnsureLayerKeys(nameof(PortImplVocabulary.CallKeys), $"ports.{portKey}.impl.calls[{index}]", item);

            var alias = Str(item, "as")
                ?? throw new StandardViolationException($"[port:standard] 端口 '{portKey}' 的 impl.calls 项缺少 as");
            var method = Str(item, "method")
                ?? throw new StandardViolationException($"[port:standard] 端口 '{portKey}' 的 calls['{alias}'] 缺少 method");

            calls.Add(new CallStandard(
                alias,
                Str(item, "target"),
                method,
                Bool(item, "property") ?? false,
                Int(item, "dep"),
                Bool(item, "await") ?? false,
                Str(item, "expect"),
                ReadComposedArgs(portKey, item)));
        }

        return calls;
    }

    /// <summary>
    /// 组合族投影声明读取（<c>impl.project[]</c>）：每项 <c>{ field, call, select, other?, path?, dep?, method?, key? }</c>；
    /// 缺 <c>field</c>/<c>call</c>/<c>select</c> 即抛（防产物字段静默丢失）。
    /// </summary>
    private static IReadOnlyList<ComposedFieldStandard> ReadProject(string portKey, JsonElement impl)
    {
        if (!impl.TryGetProperty("project", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return [];

        var fields = new List<ComposedFieldStandard>();
        var index = -1;
        foreach (var item in declared.EnumerateArray())
        {
            index++;
            EnsureLayerKeys(nameof(PortImplVocabulary.ComposedFieldKeys), $"ports.{portKey}.impl.project[{index}]", item);

            var field = Str(item, "field")
                ?? throw new StandardViolationException($"[port:standard] 端口 '{portKey}' 的 impl.project 项缺少 field");
            var call = Str(item, "call")
                ?? throw new StandardViolationException($"[port:standard] 端口 '{portKey}' 的 project['{field}'] 缺少 call");
            var select = Str(item, "select")
                ?? throw new StandardViolationException($"[port:standard] 端口 '{portKey}' 的 project['{field}'] 缺少 select");

            fields.Add(new ComposedFieldStandard(
                field, call, select,
                Str(item, "other"), Str(item, "path"),
                Int(item, "dep"), Str(item, "method"), Str(item, "key")));
        }

        return fields;
    }

    /// <summary>
    /// 组合族实参声明读取（<c>args[]</c>；依赖 / 调用共用同一形态）：
    /// <c>{ convert, argIndex?, key?, dep?, value?, elementType?, optional?, default?, nullLiteral? }</c>；
    /// 缺 <c>convert</c> 即抛（防取值口径静默漂移）。
    /// </summary>
    private static IReadOnlyList<ComposedArgStandard>? ReadComposedArgs(string portKey, JsonElement owner)
    {
        if (!owner.TryGetProperty("args", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return null;

        var args = new List<ComposedArgStandard>();
        var index = -1;
        foreach (var item in declared.EnumerateArray())
        {
            index++;
            EnsureLayerKeys(nameof(PortImplVocabulary.ComposedArgKeys), $"ports.{portKey}.args[{index}]", item);

            var convert = Str(item, "convert")
                ?? throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的 args 项缺少 convert");

            args.Add(new ComposedArgStandard(
                convert,
                Int(item, "argIndex"),
                Str(item, "key"),
                Int(item, "dep"),
                item.TryGetProperty("value", out var constant) ? constant.Clone() : null,
                Str(item, "elementType"),
                Bool(item, "optional") ?? false,
                item.TryGetProperty("default", out var fallback) ? fallback.Clone() : null,
                Bool(item, "nullLiteral") ?? false));
        }

        return args;
    }
}
