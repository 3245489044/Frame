namespace Strategy.Observability.Ports;

using System.Text.Json;

/// <summary>
/// 端口台账条目（R2-2 **唯一解析产物**）：一条 <c>ports[key]</c> 的声明视图 —— 实现归属
/// （kind / id / target / method / reason）+ 取值声明（args / binds）+ 组合声明（deps / calls / project）
/// + 脚本声明（script / scriptProject / deliver / scopes / scenarios）。
/// <para>
/// 只读驱动元数据：<see cref="LegacyKind"/> / <see cref="ProductFields"/> 供上层装配与守卫消费，
/// **求值不得读**（路线 §Q2：kind 与驱动元数据保留为只读分类信息，不得参与求值分支 —— 由守卫机检）。
/// </para>
/// </summary>
/// <param name="PortKey">端口键（不透明字符串；<c>&lt;类型/接口&gt;.&lt;方法&gt;</c> 仅渲染口径）。</param>
/// <param name="Kind">绑定方式（只读元数据：<c>delegate</c> / <c>reflective</c> / <c>composed</c> / <c>scripted</c> / <c>frozen</c>）。</param>
/// <param name="Id">委派实现的登记 id。</param>
/// <param name="Target">目标类型全名（反射绑定）。</param>
/// <param name="Method">目标方法名（反射绑定）。</param>
/// <param name="Reason">冻结 / 未绑定原因。</param>
/// <param name="LegacyKind">spec 面入口名（历史 kind；只读驱动元数据）。</param>
/// <param name="ProductFields">产物字段声明（<c>|</c> 分隔；只读驱动元数据）。</param>
/// <param name="Args">位置实参声明（位序即语义）。</param>
/// <param name="Binds">脚本输入绑定（槽名 → 取值来源）。</param>
/// <param name="Deps">依赖声明（声明序 = 构建序）。</param>
/// <param name="Calls">调用声明（声明序 = 调用序）。</param>
/// <param name="Project">组合投影声明（产物字段 ← 调用 / 替身取法）。</param>
/// <param name="Script">脚本步骤声明（声明序 = 执行序）。</param>
/// <param name="ScriptProject">脚本投影声明。</param>
/// <param name="Deliver">载体声明（value / issues / action）。</param>
/// <param name="Scopes">临时目录作用域声明。</param>
/// <param name="Scenarios">多场景探针声明（声明了才非空）。</param>
public sealed record PortLedgerEntry(
    string PortKey,
    string Kind,
    string? Id,
    string? Target,
    string? Method,
    string? Reason,
    string? LegacyKind,
    string ProductFields,
    IReadOnlyList<PortArgSpec> Args,
    IReadOnlyDictionary<string, ScriptedArgSpec> Binds,
    IReadOnlyList<ComposedDepSpec> Deps,
    IReadOnlyList<ComposedCallSpec> Calls,
    IReadOnlyList<ComposedFieldSpec> Project,
    IReadOnlyList<ScriptedStep> Script,
    IReadOnlyList<ScriptedFieldSpec> ScriptProject,
    ScriptedDeliverSpec? Deliver,
    IReadOnlyList<ScriptedScopeSpec> Scopes,
    ScriptedScenarioSpec? Scenarios);

/// <summary>
/// 端口台账**唯一解析器**（R2-2 / 路线 §2.1）：<c>json → IReadOnlyList&lt;PortLedgerEntry&gt;</c>。
/// <para>
/// 纪律：① 校验**单点**（解析期一次，D-6：上层不再各自校验）；② 只认不透明字符串与结构算子，非法形态即抛
/// （诊断前缀 <c>[port:standard]</c> 与迁移前逐字一致）；③ 零领域词汇（键 / 类型全名 / 方法名均为原样字符串）；
/// ④ 只读 JSON 文本，不读文件、不读嵌入资源（来源由宿主决定）。
/// </para>
/// <para>脚本族声明读取由 <see cref="PortLedgerScriptedReader"/> 承担（同批上移，逐字保真）。</para>
/// </summary>
public static class PortLedger
{
    /// <summary>空绑定表（非脚本绑定的条目视图）。</summary>
    private static readonly IReadOnlyDictionary<string, ScriptedArgSpec> EmptyBinds =
        new Dictionary<string, ScriptedArgSpec>(StringComparer.Ordinal);

    /// <summary>解析台账 JSON（<c>ports[key] → 条目</c>，文件序 = 声明序）。</summary>
    public static IReadOnlyList<PortLedgerEntry> Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("ports", out var ports) || ports.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("[port:standard] 标准文件缺少 ports 段（端口键 → 端口标准体）");

        var entries = new List<PortLedgerEntry>();
        foreach (var property in ports.EnumerateObject())
            entries.Add(Read(property.Name, property.Value));

        return entries;
    }

    /// <summary>单条读取（<c>impl</c> 的种类与归属 + 各族声明体）。</summary>
    private static PortLedgerEntry Read(string portKey, JsonElement spec)
    {
        // impl 缺省或为字符串（简写）：kind 取字符串值；delegate 无 id → 装配期记 Unbound（fail-safe，不静默跳过）
        if (!spec.TryGetProperty("impl", out var impl) || impl.ValueKind != JsonValueKind.Object)
        {
            var text = impl.ValueKind == JsonValueKind.String ? impl.GetString() : null;
            return Empty(portKey, NormalizeKind(portKey, text));
        }

        var kind = NormalizeKind(portKey, PortLedgerJson.Str(impl, "kind"));

        return new PortLedgerEntry(
            portKey,
            kind,
            PortLedgerJson.Str(impl, "id"),
            PortLedgerJson.Str(impl, "target"),
            PortLedgerJson.Str(impl, "method"),
            PortLedgerJson.Str(impl, "reason"),
            PortLedgerJson.Str(impl, "legacyKind"),
            PortLedgerJson.Str(impl, "productFields") ?? string.Empty,
            ReadArgs(portKey, impl),
            kind == "scripted" ? PortLedgerScriptedReader.ReadBinds(portKey, impl) : EmptyBinds,
            ReadDeps(portKey, impl),
            kind == "composed" ? ReadCalls(portKey, impl) : Array.Empty<ComposedCallSpec>(),
            kind == "composed" ? ReadProject(portKey, impl) : Array.Empty<ComposedFieldSpec>(),
            kind == "scripted" ? PortLedgerScriptedReader.ReadScript(portKey, impl) : Array.Empty<ScriptedStep>(),
            kind == "scripted" ? PortLedgerScriptedReader.ReadProject(portKey, impl) : Array.Empty<ScriptedFieldSpec>(),
            kind == "scripted" ? PortLedgerScriptedReader.ReadDeliver(portKey, impl) : null,
            kind == "scripted" ? PortLedgerScriptedReader.ReadScopes(portKey, impl) : Array.Empty<ScriptedScopeSpec>(),
            kind == "scripted" ? PortLedgerScriptedReader.ReadScenarios(portKey, impl) : null);
    }

    /// <summary>无声明体条目（<c>impl</c> 缺省 / 简写）。</summary>
    private static PortLedgerEntry Empty(string portKey, string kind)
        => new(portKey, kind, null, null, null, null, null, string.Empty,
            Array.Empty<PortArgSpec>(), EmptyBinds, Array.Empty<ComposedDepSpec>(), Array.Empty<ComposedCallSpec>(),
            Array.Empty<ComposedFieldSpec>(), Array.Empty<ScriptedStep>(), Array.Empty<ScriptedFieldSpec>(),
            null, Array.Empty<ScriptedScopeSpec>(), null);

    /// <summary>种类归一（非法即抛；诊断文案与迁移前逐字一致）。</summary>
    private static string NormalizeKind(string portKey, string? text)
        => text switch
        {
            null or "" or "delegate" => "delegate",
            "reflective" or "composed" or "scripted" or "frozen" => text,
            _ => throw new InvalidOperationException(
                $"[port:standard] 端口 '{portKey}' 的 impl.kind '{text}' 非法（delegate|reflective|composed|scripted|frozen）"),
        };

    // ── 各族声明体读取（R2-2 自 SpecHost 逐字上移）────────────────────────────────

    /// <summary>依赖声明（<c>impl.deps[]</c> / 场景主体 <c>deps[]</c>）：<c>new</c> / <c>factory</c> / <c>fake</c> 三形态之一，其余字段按形态取用。</summary>
    internal static IReadOnlyList<ComposedDepSpec> ReadDeps(string portKey, JsonElement impl)
    {
        if (!impl.TryGetProperty("deps", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return Array.Empty<ComposedDepSpec>();

        var deps = new List<ComposedDepSpec>();
        var index = -1;

        foreach (var item in declared.EnumerateArray())
        {
            index++;
            var spec = new ComposedDepSpec(
                PortLedgerJson.Str(item, "new"),
                PortLedgerJson.Str(item, "factory"),
                PortLedgerJson.Str(item, "fake"),
                PortLedgerJson.Str(item, "contract"),
                PortLedgerJson.Int(item, "wrap"),
                PortLedgerJson.StrList(item, "methods"),
                PortLedgerJson.Elements(item, "values"),
                PortLedgerJson.StrList(item, "genericArgs"),
                ReadComposedArgs(portKey, item));

            if (spec.New is null && spec.Factory is null && spec.Fake is null)
                throw new InvalidOperationException(
                    $"[port:standard] 端口 '{portKey}' 的 impl.deps[{index}] 未声明形态（须给 new / factory / fake）");

            deps.Add(spec);
        }

        return deps;
    }

    /// <summary>
    /// 组合绑定的调用声明读取（<c>impl.calls[]</c>）：每项 <c>{ as, target?, method, property?, dep?, await?, expect?, args? }</c>；
    /// 缺 <c>as/method</c> 即抛（fail-fast，防声明残缺静默绑定）。
    /// </summary>
    private static IReadOnlyList<ComposedCallSpec> ReadCalls(string portKey, JsonElement impl)
    {
        if (!impl.TryGetProperty("calls", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return Array.Empty<ComposedCallSpec>();

        var calls = new List<ComposedCallSpec>();
        foreach (var item in declared.EnumerateArray())
        {
            var alias = PortLedgerJson.Str(item, "as")
                ?? throw new InvalidOperationException($"[port:standard] 端口 '{portKey}' 的 impl.calls 项缺少 as");
            var method = PortLedgerJson.Str(item, "method")
                ?? throw new InvalidOperationException($"[port:standard] 端口 '{portKey}' 的 calls['{alias}'] 缺少 method");

            calls.Add(new ComposedCallSpec(
                alias,
                PortLedgerJson.Str(item, "target"),
                method,
                PortLedgerJson.Bool(item, "property") ?? false,
                PortLedgerJson.Int(item, "dep"),
                PortLedgerJson.Bool(item, "await") ?? false,
                PortLedgerJson.Str(item, "expect"),
                ReadComposedArgs(portKey, item)));
        }

        return calls;
    }

    /// <summary>
    /// 组合绑定的投影声明读取（<c>impl.project[]</c>）：每项 <c>{ field, call, select, other? }</c>；
    /// 缺 <c>field/call/select</c> 即抛（防产物字段静默丢失）。
    /// </summary>
    private static IReadOnlyList<ComposedFieldSpec> ReadProject(string portKey, JsonElement impl)
    {
        if (!impl.TryGetProperty("project", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return Array.Empty<ComposedFieldSpec>();

        var fields = new List<ComposedFieldSpec>();
        foreach (var item in declared.EnumerateArray())
        {
            var field = PortLedgerJson.Str(item, "field")
                ?? throw new InvalidOperationException($"[port:standard] 端口 '{portKey}' 的 impl.project 项缺少 field");
            var call = PortLedgerJson.Str(item, "call")
                ?? throw new InvalidOperationException($"[port:standard] 端口 '{portKey}' 的 project['{field}'] 缺少 call");
            var select = PortLedgerJson.Str(item, "select")
                ?? throw new InvalidOperationException($"[port:standard] 端口 '{portKey}' 的 project['{field}'] 缺少 select");

            fields.Add(new ComposedFieldSpec(
                field, call, select,
                PortLedgerJson.Str(item, "other"), PortLedgerJson.Str(item, "path"),
                PortLedgerJson.Int(item, "dep"), PortLedgerJson.Str(item, "method"), PortLedgerJson.Str(item, "key")));
        }

        return fields;
    }

    /// <summary>
    /// 组合绑定的实参声明读取（<c>args[]</c>；依赖 / 调用共用同一形态）：
    /// <c>{ convert, argIndex?, key?, dep?, value?, elementType?, optional?, default?, nullLiteral? }</c>；
    /// 缺 <c>convert</c> 即抛（防取值口径静默漂移）。
    /// </summary>
    private static IReadOnlyList<ComposedArgSpec>? ReadComposedArgs(string portKey, JsonElement owner)
    {
        if (!owner.TryGetProperty("args", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return null;

        var args = new List<ComposedArgSpec>();
        foreach (var item in declared.EnumerateArray())
        {
            var convert = PortLedgerJson.Str(item, "convert")
                ?? throw new InvalidOperationException($"[port:standard] 端口 '{portKey}' 的 args 项缺少 convert");

            args.Add(new ComposedArgSpec(
                convert,
                PortLedgerJson.Int(item, "argIndex"),
                PortLedgerJson.Str(item, "key"),
                PortLedgerJson.Int(item, "dep"),
                item.TryGetProperty("value", out var constant) ? constant.Clone() : null,
                PortLedgerJson.Str(item, "elementType"),
                PortLedgerJson.Bool(item, "optional") ?? false,
                item.TryGetProperty("default", out var fallback) ? fallback.Clone() : null,
                PortLedgerJson.Bool(item, "nullLiteral") ?? false));
        }

        return args;
    }


    private static IReadOnlyList<PortArgSpec> ReadArgs(string portKey, JsonElement impl)
    {
        if (!impl.TryGetProperty("args", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return Array.Empty<PortArgSpec>();

        var args = new List<PortArgSpec>();
        foreach (var item in declared.EnumerateArray())
        {
            var convert = PortLedgerJson.Str(item, "convert")
                ?? throw new InvalidOperationException($"[port:standard] 端口 '{portKey}' 的 impl.args 项缺少 convert");

            args.Add(new PortArgSpec(
                convert,
                PortLedgerJson.Bool(item, "optional") ?? false,
                PortLedgerJson.Bool(item, "nullLiteral") ?? false,
                item.TryGetProperty("default", out var fallback) ? fallback.Clone() : null,
                PortLedgerJson.Str(item, "from"),
                PortLedgerJson.Str(item, "key")));
        }

        return args;
    }
}