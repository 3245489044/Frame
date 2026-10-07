namespace DataCenter.Standards;

using System.Text.Json;
using DataCenter.Protocol.Standards;
using static Plate.JsonMethod.Utilities.JsonTolerantReader;

/// <summary>
/// 脚本族 <c>impl</c> 读取（<c>bind / script / project / deliver / scopes / scenarios</c>；B3 自内核
/// <c>PortLedgerScriptedReader</c> **逐字迁移**，仅类型改名 <c>Scripted* → *Standard</c>）：
/// 只认不透明字符串与结构算子，非法形态即抛（诊断前缀 <c>[port:standard]</c> 与迁移前逐字一致）。
/// <para>
/// 分工：本件只回答「JSON 里写了什么」（标准层职责）；「怎么变成可调用边界」仍是内核编译面的职责；
/// 零领域词汇：端口键 / 类型全名 / 方法名 / 字段名均为原样字符串。
/// </para>
/// </summary>
public static partial class StandardJson
{
    /// <summary>输入绑定读取（<c>impl.bind</c>：槽名 → 取值来源）。</summary>
    internal static IReadOnlyDictionary<string, ValueSourceStandard> ReadBinds(string portKey, JsonElement impl)
    {
        var binds = new Dictionary<string, ValueSourceStandard>(StringComparer.Ordinal);
        if (!impl.TryGetProperty("bind", out var declared) || declared.ValueKind != JsonValueKind.Object)
            return binds;

        foreach (var property in declared.EnumerateObject())
            binds[property.Name] = ReadArg(portKey, property.Value, $"bind['{property.Name}']");

        return binds;
    }

    /// <summary>步骤读取（<c>impl.script[]</c>：new / invoke / foreach，声明序 = 执行序）。</summary>
    internal static IReadOnlyList<StepStandard> ReadScript(string portKey, JsonElement impl)
    {
        if (!impl.TryGetProperty("script", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return Array.Empty<StepStandard>();

        var steps = new List<StepStandard>();
        foreach (var item in declared.EnumerateArray()) steps.Add(ReadStep(portKey, item));

        return steps;
    }

    /// <summary>投影读取（<c>impl.project[]{ field, expr }</c>）。</summary>
    internal static IReadOnlyList<FieldExprStandard> ReadScriptProject(string portKey, JsonElement impl)
    {
        if (!impl.TryGetProperty("project", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return Array.Empty<FieldExprStandard>();

        var fields = new List<FieldExprStandard>();
        foreach (var item in declared.EnumerateArray())
        {
            // 规范书写形态 = `{ field, expr: { …算子 } }`；`expr` 缺省时算子可直接写在项上（简写）。
            // 纯动态键投影 = `{ spread: { … } }`（无 field：展开为产物动态键，不占字段位，见 W12）。
            var body = item.TryGetProperty("expr", out var expression) ? expression : item;

            var field = Str(item, "field");
            var isSpread = body.TryGetProperty("spread", out var spread) && spread.ValueKind == JsonValueKind.Object;
            if (field is null && !isSpread)
                throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的 impl.project 项缺 field（或声明 spread 动态键投影）");

            fields.Add(new FieldExprStandard(
                field ?? string.Empty, ReadExpr(portKey, body, $"project['{field ?? "spread"}']")));
        }

        return fields;
    }

    /// <summary>
    /// 作用域读取（<c>impl.scopes[]</c>）：<c>kind</c> ∈ <c>tempDir</c>（临时目录，P2）｜<c>using</c>（资源释放，W18）｜
    /// <c>restore</c>（静态槽还原，W27）；<c>as</c> 必填。缺失 → 空序列。
    /// </summary>
    internal static IReadOnlyList<ScopeStandard> ReadScopes(string portKey, JsonElement impl)
    {
        if (!impl.TryGetProperty("scopes", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return Array.Empty<ScopeStandard>();

        var scopes = new List<ScopeStandard>();
        foreach (var item in declared.EnumerateArray())
        {
            EnsureLayerKeys(nameof(PortImplVocabulary.ScopeKeys), $"ports.{portKey}.impl.scopes", item);
            var kind = Str(item, "kind") ?? "tempDir";
            var asName = Str(item, "as")
                ?? throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的 impl.scopes 项缺 as（作用域名）");

            scopes.Add(new ScopeStandard(
                kind,
                asName,
                Str(item, "prefix"),
                ReadFiles(portKey, item, asName),
                item.TryGetProperty("invoke", out var invoke) && invoke.ValueKind == JsonValueKind.Object
                    ? ReadInvoke(portKey, invoke)
                    : null,
                ReadSaves(portKey, item, asName),
                item.TryGetProperty("open", out var open) && open.ValueKind == JsonValueKind.Array
                    ? ReadSteps(portKey, item, "open")
                    : null,
                item.TryGetProperty("close", out var close) && close.ValueKind == JsonValueKind.Array
                    ? ReadSteps(portKey, item, "close")
                    : null));
        }

        return scopes;
    }

    /// <summary>进入期留存项读取（<c>save:[{ slot, type, key, genericArgs? }]</c>，W27）。</summary>
    private static IReadOnlyList<SaveStandard>? ReadSaves(string portKey, JsonElement scope, string asName)
    {
        if (!scope.TryGetProperty("save", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return null;

        var saves = new List<SaveStandard>();
        foreach (var item in declared.EnumerateArray())
        {
            var slot = Str(item, "slot")
                ?? throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的作用域 '{asName}' 的 save 项缺 slot（留存槽名）");
            var type = Str(item, "type")
                ?? throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的作用域 '{asName}' 的 save 项 '{slot}' 缺 type（类型全名）");
            var key = Str(item, "key")
                ?? throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的作用域 '{asName}' 的 save 项 '{slot}' 缺 key（成员名）");

            saves.Add(new SaveStandard(slot, type, key, StrList(item, "genericArgs")));
        }

        return saves;
    }

    /// <summary>作用域预置文件读取（<c>files[]</c>：<c>name</c> / <c>dir</c> / <c>from</c> / <c>content</c> / <c>when</c>）。</summary>
    private static IReadOnlyList<FileStandard>? ReadFiles(string portKey, JsonElement scope, string asName)
    {
        if (!scope.TryGetProperty("files", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return null;

        var files = new List<FileStandard>();
        foreach (var item in declared.EnumerateArray())
        {
            var name = Str(item, "name")
                ?? throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的作用域 '{asName}' 的文件项缺 name（文件名）");

            var from = item.TryGetProperty("from", out var declaredFrom) && declaredFrom.ValueKind == JsonValueKind.Object
                ? ReadArg(portKey, declaredFrom, $"作用域 '{asName}' 的文件 '{name}' 的 from")
                : null;

            files.Add(new FileStandard(
                name,
                item.TryGetProperty("dir", out var dir) ? ReadExpr(portKey, dir, $"作用域 '{asName}' 的文件 '{name}' 的 dir") : null,
                from,
                item.TryGetProperty("content", out var content)
                    ? ReadExpr(portKey, content, $"作用域 '{asName}' 的文件 '{name}' 的 content")
                    : null,
                item.TryGetProperty("when", out var gate)
                    ? ReadExpr(portKey, gate, $"作用域 '{asName}' 的文件 '{name}' 的 when")
                    : null));
        }

        return files;
    }

    /// <summary>载体读取（<c>impl.deliver</c>，P4）：<c>kind ∈ {value, issues, action}</c>；旧布尔（<c>defer</c> / <c>issues</c>）
    /// 兼容保留；<c>issues</c> 支持 <c>capture{call,expect,message,messageField,when}</c>，
    /// <c>action</c> 支持 <c>defer.steps[]</c>（断言期执行的步骤），<c>value</c> 支持 <c>value</c> 表达式（W11）。
    /// </summary>
    internal static DeliverStandard? ReadDeliver(string portKey, JsonElement impl)
    {
        if (!impl.TryGetProperty("deliver", out var declared) || declared.ValueKind != JsonValueKind.Object)
            return null;

        EnsureLayerKeys(nameof(PortImplVocabulary.DeliverKeys), $"ports.{portKey}.impl.deliver", declared);
        var defer = Bool(declared, "defer") ?? false;
        var issues = Bool(declared, "issues") ?? false;
        if (defer && issues)
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 impl.deliver 载体 defer 与 issues 互斥（二选一）");

        return new DeliverStandard(
            Str(declared, "kind"),
            defer,
            issues,
            Str(declared, "issueField"),
            ReadCapture(portKey, declared),
            ReadDeferSteps(portKey, declared),
            declared.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object
                ? ReadExpr(portKey, value, "deliver.value")
                : null);
    }

    /// <summary>issue 捕获体读取（<c>deliver.capture{call,expect,message,messageField,when}</c>）。</summary>
    private static DeliverCaptureStandard? ReadCapture(string portKey, JsonElement deliver)
    {
        if (!deliver.TryGetProperty("capture", out var capture) || capture.ValueKind != JsonValueKind.Object)
            return null;

        var invoke = capture.TryGetProperty("call", out var call) && call.ValueKind == JsonValueKind.Object
            ? ReadInvoke(portKey, call)
            : new InvokeStandard(
                As: Str(capture, "as"),
                Dep: Int(capture, "dep"),
                Target: Str(capture, "target"),
                Method: call.ValueKind == JsonValueKind.String
                    ? call.GetString()!
                    : Str(capture, "method")
                      ?? throw new StandardViolationException(
                          $"[port:standard] 端口 '{portKey}' 的 impl.deliver.capture 缺 call（被捕获的调用对象或方法名）"),
                Await: Bool(capture, "await") ?? false,
                Args: ReadArgs(portKey, capture, "deliver.capture"),
                Out: Str(capture, "out"));

        return new DeliverCaptureStandard(
            invoke,
            StrList(capture, "expect") ?? Array.Empty<string>(),
            EnsureKnownMessageMode(portKey, Str(capture, "message"), "deliver.capture.message"),
            Str(capture, "messageField"),
            capture.TryGetProperty("when", out var gate) ? ReadExpr(portKey, gate, "deliver.capture.when") : null);
    }

    /// <summary>延迟动作步骤读取（<c>deliver.defer.steps[]</c>；旧布尔形态下为 null = 整脚本包装）。</summary>
    private static IReadOnlyList<StepStandard>? ReadDeferSteps(string portKey, JsonElement deliver)
        => deliver.TryGetProperty("defer", out var deferred)
           && deferred.ValueKind == JsonValueKind.Object
           && deferred.TryGetProperty("steps", out var steps)
           && steps.ValueKind == JsonValueKind.Array
            ? steps.EnumerateArray().Select(item => ReadStep(portKey, item)).ToArray()
            : null;

    /// <summary>单步读取：形态四选一（new / invoke / foreach / throw）+ 修饰（collect / when）。</summary>
    private static StepStandard ReadStep(string portKey, JsonElement item)
    {
        EnsureLayerKeys(nameof(PortImplVocabulary.StepKeys), $"ports.{portKey}.impl.script.step", item);
        var collect = ReadCollects(portKey, item);
        var when = item.TryGetProperty("when", out var condition)
            ? ReadExpr(portKey, condition, "步骤 when")
            : null;

        // 纯值源步骤（集合字面量）：须先于 foreach 判别 —— list 步骤同层带 items 键。
        if (Str(item, "from") is "list" or "map")
            return ReadValueSourceStep(portKey, item, collect, when);

        if (item.TryGetProperty("foreach", out _) || item.TryGetProperty("items", out _))
            return new StepStandard(collect, when, Foreach: ReadForeach(portKey, item));

        if (item.TryGetProperty("new", out var construct) && construct.ValueKind == JsonValueKind.Object)
            return new StepStandard(collect, when, New: ReadNew(portKey, construct));

        if (item.TryGetProperty("invoke", out var invoke) && invoke.ValueKind == JsonValueKind.Object)
            return new StepStandard(collect, when, Invoke: ReadInvoke(portKey, invoke));

        if (item.TryGetProperty("throw", out var raised) && raised.ValueKind == JsonValueKind.Object)
            return new StepStandard(collect, when, Throw: ReadThrow(portKey, raised));


        throw new StandardViolationException(
            $"[port:standard] 端口 '{portKey}' 的 impl.script 步骤未声明形态（new|invoke|foreach|throw|from:list|from:map）");
    }

    /// <summary>
    /// 纯值源步骤读取（<c>script[].from = list|map</c> 集合字面量）：步骤同层即值源声明 + <c>as</c> 写槽（缺即抛）。
    /// </summary>
    private static StepStandard ReadValueSourceStep(
        string portKey, JsonElement item, IReadOnlyList<CollectStandard>? collect, ExprStandard? when)
    {
        var asName = Str(item, "as");
        if (string.IsNullOrWhiteSpace(asName))
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 impl.script 值源步骤缺 as（写槽名）");

        var source = ReadArg(portKey, item, $"impl.script 值源步骤 '{asName}'", StepModifierKeys);
        return new StepStandard(collect, when, Source: source, As: asName);
    }

    /// <summary>步骤同层修饰键（值源步骤形态下与值源键并存：collect / when / as）。</summary>
    private static readonly IReadOnlyList<string> StepModifierKeys = ["collect", "when", "as"];

    /// <summary>
    /// 主动抛出步骤读取（<c>script[].throw</c>；G3）：<c>{ type, message? }</c>；
    /// 缺 <c>type</c> 即抛（防声明残缺静默绑定）。
    /// </summary>
    private static ThrowStandard ReadThrow(string portKey, JsonElement raised)
    {
        EnsureLayerKeys(nameof(PortImplVocabulary.ThrowKeys), $"ports.{portKey}.impl.script.throw", raised);

        var type = Str(raised, "type");

        return string.IsNullOrEmpty(type)
            ? throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 impl.script 步骤 throw 缺 type（异常类型全名）")
            : new ThrowStandard(type, Str(raised, "message"));
    }


    /// <summary>
    /// 收集声明读取（<c>collect</c>）：字符串 = 槽名（步产物 → 该槽）；数组 = 多条
    /// <c>{ slot, from? }</c>（<c>from</c> 非空 = 取该槽当前值，供 <c>invoke.out</c> 出参逐迭代留存）。
    /// </summary>
    private static IReadOnlyList<CollectStandard>? ReadCollects(string portKey, JsonElement item)
    {
        if (!item.TryGetProperty("collect", out var declared)) return null;

        if (declared.ValueKind == JsonValueKind.String)
            return [new CollectStandard(declared.GetString()!)];

        if (declared.ValueKind != JsonValueKind.Array)
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 impl.script 步骤 collect 须为槽名或 collect 声明数组");

        var collects = new List<CollectStandard>();
        foreach (var entry in declared.EnumerateArray())
        {
            var slot = Str(entry, "slot")
                ?? throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的 impl.script 步骤 collect 项缺 slot（收集槽名）");
            collects.Add(new CollectStandard(slot, Str(entry, "from")));
        }

        return collects;
    }

    /// <summary>构造读取（<c>script[].new</c> 与实参内嵌 <c>construct</c> 共用；<c>as</c> 缺省 = 不写槽）。</summary>
    private static NewStandard ReadNew(string portKey, JsonElement body)
    {
        var asName = Str(body, "as") ?? string.Empty;
        var type = Str(body, "type")
            ?? throw new StandardViolationException($"[port:standard] 端口 '{portKey}' 的构造声明缺 type（类型全名）");

        return new NewStandard(
            asName, type,
            StrList(body, "genericArgs"),
            ReadArgs(portKey, body, $"构造 '{type}'"),
            ReadChain(portKey, body),
            ReadInit(portKey, body),
            Str(body, "expect"));
    }

    /// <summary>
    /// 对象初始化器读取（W13：<c>init:[{ member, value, key? }]</c>；声明序 = 赋值序；缺 value 即抛）。
    /// <c>key</c>（G7）给出时 = 容器写入键（写入 <c>member[key] = value</c>）。
    /// </summary>
    private static IReadOnlyList<InitStandard>? ReadInit(string portKey, JsonElement body)
    {
        if (!body.TryGetProperty("init", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return null;

        var items = new List<InitStandard>();
        foreach (var item in declared.EnumerateArray())
        {
            var member = Str(item, "member")
                ?? throw new StandardViolationException($"[port:standard] 端口 '{portKey}' 的 init 项缺 member（成员名）");

            var key = item.TryGetProperty("key", out var keySource)
                ? ReadArg(portKey, keySource, $"init '{member}' 的 key")
                : null;

            if (item.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object)
                items.Add(new InitStandard(member, ReadArg(portKey, value, $"init '{member}'"), key));
            else
                throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的 init 成员 '{member}' 缺 value（取值声明对象）");
        }

        return items;
    }

    /// <summary>链式后置调用读取（W6：<c>chain:[{ method, args?, rebind? }]</c>；链值默认为实例本身）。</summary>
    private static IReadOnlyList<ChainStandard>? ReadChain(string portKey, JsonElement body)
    {
        if (!body.TryGetProperty("chain", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return null;

        var links = new List<ChainStandard>();
        foreach (var item in declared.EnumerateArray())
        {
            var method = Str(item, "method")
                ?? throw new StandardViolationException($"[port:standard] 端口 '{portKey}' 的 chain 项缺 method");
            links.Add(new ChainStandard(
                method,
                ReadArgs(portKey, item, $"chain '{method}'"),
                Bool(item, "rebind") ?? false));
        }

        return links;
    }

    /// <summary>
    /// 调用步骤读取（<c>script[].invoke</c>；<c>genericArgs</c> = 开放泛型方法闭合实参，W4；
    /// <c>on</c> = 调用**目标取值**（W24：委托 / 实例值，如「对遍历变量的方法调用」「调用传入的委托」）；
    /// <c>nonPublic</c> = 含非公开成员解析，W20）。
    /// </summary>
    private static InvokeStandard ReadInvoke(string portKey, JsonElement body)
    {
        var method = Str(body, "method")
            ?? throw new StandardViolationException($"[port:standard] 端口 '{portKey}' 的 script.invoke 缺 method");

        var target = body.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.Object
            ? ReadArg(portKey, on, $"script.invoke '{method}' 的目标取值")
            : null;

        return new InvokeStandard(
            Str(body, "as"),
            Int(body, "dep"),
            Str(body, "target"),
            method,
            Bool(body, "property") ?? false,
            Bool(body, "await") ?? false,
            Str(body, "expect"),
            ReadArgs(portKey, body, $"script.invoke '{method}'"),
            Str(body, "out"),
            Bool(body, "break") ?? false,
            StrList(body, "genericArgs"),
            target,
            Bool(body, "nonPublic") ?? false,
            Bool(body, "keepTask") ?? false);
    }

    /// <summary>
    /// 多场景探针读取（W2：<c>scenarios:{ select, cases:{ "&lt;场景名&gt;": { binds / deps / script / project / deliver / scopes } } }</c>）。
    /// <para><c>select</c> 缺省 = 具名实参 <c>probe</c>（可选，未给即「未命中」）；<c>cases</c> 缺即抛（防静默空场景表）。</para>
    /// </summary>
    internal static ScenarioStandard? ReadScenarios(string portKey, JsonElement impl)
    {
        if (!impl.TryGetProperty("scenarios", out var scenarios) || scenarios.ValueKind != JsonValueKind.Object)
            return null;

        if (!scenarios.TryGetProperty("cases", out var declared) || declared.ValueKind != JsonValueKind.Object)
            throw new StandardViolationException($"[port:standard] 端口 '{portKey}' 的 scenarios 缺 cases（场景表）");

        var select = scenarios.TryGetProperty("select", out var selector)
            ? ReadArg(portKey, selector, "scenarios.select")
            : new ValueSourceStandard(From: "arg", Index: 0, Optional: true);

        var cases = new Dictionary<string, ScenarioBodyStandard>(StringComparer.Ordinal);
        foreach (var item in declared.EnumerateObject())
        {
            var body = item.Value;
            cases[item.Name] = new ScenarioBodyStandard(
                ReadBinds(portKey, body),
                ReadDeps(portKey, body),
                ReadScript(portKey, body),
                ReadScriptProject(portKey, body),
                ReadDeliver(portKey, body),
                ReadScopes(portKey, body));
        }

        return new ScenarioStandard(select, cases);
    }

    /// <summary>
    /// 遍历步骤读取（<c>script[].foreach</c> + <c>items</c> / <c>as</c> / <c>where</c> / <c>do</c>）。
    /// <para>
    /// 形状单义（D3）：<c>items</c> = 集合表达式（**唯一推荐形态**）；<c>foreach</c> = 兼容的集合**槽名**形态，
    /// 仅在其后无 <c>items</c> 时接受；两者同声明 = 二义 → 拒（不静默取其一）。
    /// </para>
    /// </summary>
    private static ForeachStandard ReadForeach(string portKey, JsonElement body)
    {
        var asName = Str(body, "as")
            ?? throw new StandardViolationException($"[port:standard] 端口 '{portKey}' 的 script.foreach 缺 as（循环变量名）");

        var slot = Str(body, "foreach");
        var hasItems = body.TryGetProperty("items", out var declared);

        if (slot is not null && hasItems)
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 script.foreach '{asName}' 同时声明了 foreach（集合槽 '{slot}'）" +
                "与 items（集合表达式）：二者互斥（集合来源二义），保留 items、删除 foreach");

        var items = hasItems ? ReadExpr(portKey, declared, "script.foreach.items") : null;

        if (slot is null && items is null)
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 script.foreach '{asName}' 缺集合来源（items 集合表达式）");

        var where = body.TryGetProperty("where", out var filter)
            ? ReadExpr(portKey, filter, $"script.foreach '{asName}' 的 where")
            : null;

        return new ForeachStandard(slot, items, asName, where, ReadSteps(portKey, body, "do"));
    }

    /// <summary>步骤数组读取（<c>do[]</c>；缺失 → 空序列）。</summary>
    private static IReadOnlyList<StepStandard> ReadSteps(string portKey, JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var declared) || declared.ValueKind != JsonValueKind.Array)
            return Array.Empty<StepStandard>();

        return declared.EnumerateArray().Select(item => ReadStep(portKey, item)).ToArray();
    }

    /// <summary>步骤实参读取（<c>args[]</c>；缺失 → null = 全默认值）。</summary>
    private static IReadOnlyList<ValueSourceStandard>? ReadArgs(string portKey, JsonElement owner, string context)
    {
        if (!owner.TryGetProperty("args", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return null;

        return declared.EnumerateArray()
            .Select(item => ReadArg(portKey, item, $"{context} 实参"))
            .ToArray();
    }
    /// <summary>取值来源读取（<c>bind</c> / 步骤实参 / 表达式源共用口径：<c>from</c> 显式或按键简写推断）。</summary>
    internal static ValueSourceStandard ReadArg(
        string portKey, JsonElement element, string context, IReadOnlyList<string>? extraKeys = null)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new StandardViolationException($"[port:standard] 端口 '{portKey}' 的 {context} 取值来源须为对象");

        if (PortImplVocabulary.KeysOf(nameof(PortImplVocabulary.ValueSourceKeys)) is { Count: > 0 } known)
        {
            var allowed = extraKeys is { Count: > 0 }
                ? known.Concat(extraKeys).Distinct(StringComparer.Ordinal).ToArray()
                : known;
            EnsureKnownKeys(context, element, allowed);
        }

        var from = Str(element, "from");
        var index = Int(element, "index");
        var key = Str(element, "key");
        var dep = Int(element, "dep");
        JsonElement? value = element.TryGetProperty("value", out var constant) ? constant.Clone() : null;

        // 嵌套简写：`{ "table": { "from": "with", "key": "pairs", … } }` / `{ "presence": { … } }`
        // （内层对象即行集 / 词元集来源；见规范 §4.3/§4.4）。
        // list / map 同口径：`{ "list": { "items": [ … ] } }` / `{ "map": { "pairs": [ … ] } }`。
        var owner = element;
        if (from is null && element.TryGetProperty("table", out var table) && table.ValueKind == JsonValueKind.Object)
        {
            from = "table";
            owner = table;
        }
        else if (from is null && element.TryGetProperty("presence", out var presence)
                 && presence.ValueKind == JsonValueKind.Object)
        {
            from = "presence";
            owner = presence;
        }
        else if (from is null && element.TryGetProperty("list", out var listBody)
                 && listBody.ValueKind == JsonValueKind.Object)
        {
            from = "list";
            owner = listBody;
        }
        else if (from is null && element.TryGetProperty("map", out var mapBody)
                 && mapBody.ValueKind == JsonValueKind.Object)
        {
            from = "map";
            owner = mapBody;
        }

        var rows = from == "table" ? RowSource(portKey, owner, context) : null;
        var items = from == "presence" ? TokenSource(portKey, owner, context) : null;
        var listItems = from == "list" ? ListItemSources(portKey, owner, context) : null;
        var pairs = from == "map" ? PairSources(portKey, owner, context) : null;

        if (from is null)
        {
            if (element.TryGetProperty("arg", out var positional))
            {
                from = "arg";
                index = positional.ValueKind == JsonValueKind.Number ? positional.GetInt32() : index;
            }
            else if (element.TryGetProperty("with", out var named))
            {
                from = "with";
                key = named.ValueKind == JsonValueKind.String ? named.GetString() : key;
            }
            else if (element.TryGetProperty("var", out var slot) || element.TryGetProperty("slot", out slot))
            {
                from = "var";
                key = slot.ValueKind == JsonValueKind.String ? slot.GetString() : key;
            }
            else if (element.TryGetProperty("item", out var loop))
            {
                from = "item";
                key = loop.ValueKind == JsonValueKind.String ? loop.GetString() : key;
            }
            else if (element.TryGetProperty("literal", out var literal))
            {
                from = "literal";
                value = literal.Clone();
            }
        }
        else if (from == "literal" && value is null && element.TryGetProperty("literal", out var shortLiteral))
        {
            value = shortLiteral.Clone();
        }

        return new ValueSourceStandard(
            from, index, key, dep,
            Str(element, "path"),
            Str(element, "convert"),
            Bool(element, "optional") ?? false,
            element.TryGetProperty("default", out var fallback) ? fallback.Clone() : null,
            Bool(element, "nullLiteral") ?? false,
            value,
            Str(element, "type"),
            StrList(element, "genericArgs"),
            Scope: Str(element, "scope"),
            Name: Str(element, "name"),
            Rows: rows,
            Items: items,
            RowValue: from == "table" ? Sub(portKey, owner, "rowValue", context) : null,
            Filter: from is "table" or "presence" ? Sub(portKey, owner, "where", context) : null,
            Separator: from == "table" ? Str(owner, "separator") : null,
            OnBlankFirstKey: from == "table" ? Str(owner, "onBlankFirstKey") : null,
            OnDuplicate: from == "table" ? Str(owner, "onDuplicate") : null,
            Distinct: from == "presence" && (Bool(owner, "distinct") ?? false),
            ElementType: Str(element, "elementType"),
            Construct: ReadConstruct(portKey, element),
            Named: Str(element, "named"),
            Delegate: ReadDelegate(portKey, element, context),
            Json: Bool(element, "json"),
            Content: Str(element, "content"),
            ListItems: listItems,
            Pairs: pairs);
    }

    /// <summary>委托工厂读取（W26：<c>delegate:{ kind, steps?, result?, parameters?, collect?, await? }</c>；缺省 null）。</summary>
    private static DelegateStandard? ReadDelegate(string portKey, JsonElement element, string context)
    {
        if (!element.TryGetProperty("delegate", out var declared) || declared.ValueKind != JsonValueKind.Object)
            return null;

        if (!declared.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 {context} 的 delegate 缺 kind（委托类型全名）");

        return new DelegateStandard(
            kind.GetString(),
            declared.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array
                ? steps.EnumerateArray().Select(item => ReadStep(portKey, item)).ToArray()
                : null,
            declared.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object
                ? ReadExpr(portKey, result, $"{context}.delegate.result")
                : null,
            StrList(declared, "parameters"),
            Str(declared, "collect"),
            Bool(declared, "await") ?? false);
    }

    /// <summary>实参内嵌构造读取（W6：<c>construct</c> / <c>new</c> 子对象；缺省 null）。</summary>
    private static NewStandard? ReadConstruct(string portKey, JsonElement element)
    {
        if (element.TryGetProperty("construct", out var declared) && declared.ValueKind == JsonValueKind.Object)
            return ReadNew(portKey, declared);

        return element.TryGetProperty("new", out var nested) && nested.ValueKind == JsonValueKind.Object
            ? ReadNew(portKey, nested)
            : null;
    }

    /// <summary>行表行集来源（<c>rows</c> 子对象优先；嵌套 <c>table</c> 简写下内层对象即行集来源）。</summary>
    /// <remarks>
    /// 迁移保真注记（R2-2）：原实现为 <c>ReferenceEquals(owner, element) ? throw : …</c>，而
    /// <see cref="JsonElement"/> 是值类型 —— 装箱比较恒为 false，故「缺行集来源」分支**从未可达**。
    /// 本处按**既有行为**（永不触发该断言）等价改写以消除 CA2013，并把该历史口径缺陷登记待裁决（见 R2 落地记录）；
    /// 原 `element` 形参只服务于那条恒不可达的分支，随等价改写一并去除。
    /// </remarks>
    private static ValueSourceStandard RowSource(string portKey, JsonElement owner, string context)
    {
        if (owner.TryGetProperty("rows", out var declared))
            return ReadArg(portKey, declared, $"{context} 的行集");

        return ReadArg(portKey, owner, $"{context} 的行集");
    }

    /// <summary>判定表词元集来源（<c>items</c> 子对象优先；嵌套简写下内层对象即词元集来源）。</summary>
    /// <remarks>迁移保真注记同 <see cref="RowSource"/>（原「缺词元集来源」分支对值类型恒不可达）。</remarks>
    private static ValueSourceStandard TokenSource(string portKey, JsonElement owner, string context)
    {
        if (owner.TryGetProperty("items", out var declared))
            return ReadArg(portKey, declared, $"{context} 的词元集");

        return ReadArg(portKey, owner, $"{context} 的词元集");
    }

    /// <summary>列表字面量元素（<c>from:list</c> 的 <c>items</c>：值源数组；缺 / 非数组即抛）。</summary>
    private static IReadOnlyList<ValueSourceStandard> ListItemSources(string portKey, JsonElement owner, string context)
    {
        if (!owner.TryGetProperty("items", out var declared) || declared.ValueKind != JsonValueKind.Array)
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 {context} 的 list 来源缺 items 数组（元素值源序列）");

        return declared.EnumerateArray()
            .Select((item, index) => ReadArg(portKey, item, $"{context} 的 list 第 {index + 1} 元素"))
            .ToArray();
    }

    /// <summary>字典字面量键值对（<c>from:map</c> 的 <c>pairs</c>：<c>[键值源, 值值源]</c> 二元素数组序列）。</summary>
    private static IReadOnlyList<PairStandard> PairSources(string portKey, JsonElement owner, string context)
    {
        if (!owner.TryGetProperty("pairs", out var declared) || declared.ValueKind != JsonValueKind.Array)
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 {context} 的 map 来源缺 pairs 数组（[键, 值] 序列）");

        var result = new List<PairStandard>();
        var index = 0;
        foreach (var pair in declared.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2)
                throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的 {context} 的 map 第 {index + 1} 对须为 [键, 值] 二元素数组");

            result.Add(new PairStandard(
                ReadArg(portKey, pair[0], $"{context} 的 map 第 {index + 1} 对的键"),
                ReadArg(portKey, pair[1], $"{context} 的 map 第 {index + 1} 对的值")));
            index++;
        }

        return result;
    }

    /// <summary>表达式是否声明了源值（<c>from</c> / 槽 / 实参 / 遍历变量 / 依赖 / 常量 / 内嵌构造）。</summary>
    private static bool HasSource(JsonElement element)
        => element.TryGetProperty("from", out _) || element.TryGetProperty("slot", out _)
           || element.TryGetProperty("arg", out _) || element.TryGetProperty("with", out _)
           || element.TryGetProperty("var", out _) || element.TryGetProperty("item", out _)
           || element.TryGetProperty("dep", out _) || element.TryGetProperty("literal", out _)
           || element.TryGetProperty("table", out _) || element.TryGetProperty("presence", out _)
           || element.TryGetProperty("construct", out _) || element.TryGetProperty("new", out _);

    /// <summary>
    /// 投影表达式读取（源值 + 算子；算子键见规范 §5）：<c>count/notNull/text/typeName/message/thrown/
    /// concat(+separator)/contains(+of)/all/any</c>（数组 = 逻辑合取/析取；对象 = 量词 <c>over/call/callTarget/allIn</c>）<c>/
    /// isNull/not/eq/ne/gt/ge/lt/le/spread(+except)/countEqual(a,b)/collect(+path/text/sameAsLast)</c>。
    /// </summary>
    internal static ExprStandard ReadExpr(string portKey, JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new StandardViolationException($"[port:standard] 端口 '{portKey}' 的 {context} 表达式须为对象");

        var collect = Str(element, "collect");

        // all / any 双形态：数组 = 逻辑合取 / 析取（P1 谓词）；对象 = 量词（C5 既有语义）。
        var allIsList = element.TryGetProperty("all", out var allBody) && allBody.ValueKind == JsonValueKind.Array;
        var anyIsList = element.TryGetProperty("any", out var anyBody) && anyBody.ValueKind == JsonValueKind.Array;

        string? allCall = null;
        string? allTarget = null;
        string? anyCall = null;
        string? anyTarget = null;
        ExprStandard? allIn = null;
        ExprStandard? anyIn = null;

        var all = allIsList
            ? null
            : Quantify(portKey, element, "all", context, out allCall, out allTarget, out allIn);
        var any = anyIsList
            ? null
            : Quantify(portKey, element, "any", context, out anyCall, out anyTarget, out anyIn);
        var first = First(portKey, element, context);
        var last = Last(portKey, element, context);
        var agg = Aggregate(portKey, element, context);
        var filter = Filter(portKey, element, context);
        var dict = Dict(portKey, element, context);

        if (all is not null && any is not null)
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 {context} 不能同时声明 all 与 any（量词形态互斥）");

        if (allIsList && anyIsList)
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 {context} 不能同时声明 all 与 any（合取 / 析取互斥）");

        var concat = ExprList(portKey, element, "concat", context);
        var parts = ReadConcatParts(portKey, element, context);
        var foldOver = Fold(portKey, element, context, out var foldAs, out var foldInit, out var foldStep);

        return new ExprStandard(
            Source: HasSource(element) ? ReadArg(portKey, element, context) : null,
            Path: Str(element, "path"),
            Count: Sub(portKey, element, "count", context),
            NotNull: Sub(portKey, element, "notNull", context),
            AsText: collect is null ? Sub(portKey, element, "text", context) : null,
            TypeName: Sub(portKey, element, "typeName", context),
            Message: Sub(portKey, element, "message", context),
            Thrown: Sub(portKey, element, "thrown", context),
            Concat: parts is null ? concat : null,
            Separator: Str(element, "separator"),
            Contains: Sub(portKey, element, "contains", context),
            Of: Sub(portKey, element, "of", context),
            All: all,
            Any: any,
            AllIn: allIn ?? anyIn,
            Call: allCall ?? anyCall,
            CallTarget: allTarget ?? anyTarget,
            IsNull: Sub(portKey, element, "isNull", context),
            Not: Sub(portKey, element, "not", context),
            Eq: Pair(portKey, element, "eq", context),
            Ne: Pair(portKey, element, "ne", context),
            Gt: Pair(portKey, element, "gt", context),
            Ge: Pair(portKey, element, "ge", context),
            Lt: Pair(portKey, element, "lt", context),
            Le: Pair(portKey, element, "le", context),
            AllOf: ExprList(portKey, element, "all", context),
            AnyOf: ExprList(portKey, element, "any", context),
            Invoke: element.TryGetProperty("invoke", out var call) && call.ValueKind == JsonValueKind.Object
                ? ReadInvoke(portKey, call)
                : null,
            Spread: Sub(portKey, element, "spread", context),
            Except: StrList(element, "except"),
            CountEqual: CountEqual(portKey, element, context),
            Collect: collect,
            CollectText: collect is not null && (Bool(element, "text") ?? false),
            SameAsLast: Str(element, "sameAsLast"),
            First: first.Over,
            FirstWhere: first.Where,
            FirstPath: first.Path,
            FirstOrNull: first.OrNull,
            Last: last.Over,
            LastWhere: last.Where,
            LastPath: last.Path,
            LastOrNull: last.OrNull,
            Dict: dict.Over,
            DictKey: dict.Key,
            DictValue: dict.Value,
            DictWhere: dict.Where,
            DictOnDuplicate: dict.OnDuplicate,
            Agg: agg.Over,
            AggOp: agg.Op,
            AggPath: agg.Path,
            Filter: filter.Over,
            FilterWhere: filter.Where,
            FilterPath: filter.Path,
            SameReference: Pair(portKey, element, "sameReference", context),
            Coalesce: Pair(portKey, element, "coalesce", context),
            Parts: parts,
            Distinct: Sub(portKey, element, "distinct", context),
            SequenceEqual: Pair(portKey, element, "sequenceEqual", context),
            Fold: foldOver,
            FoldAs: foldAs,
            FoldInit: foldInit,
            FoldStep: foldStep,
            Capture: ReadCapture(element),
            CaptureCount: ReadCaptureCount(element),
            FakeCount: ReadFakeCount(element));
    }

    /// <summary>捕获取值节点读取（收敛 3）：<c>capture:{ dep, member?, at?, index?, path?, orNull? }</c>。</summary>
    private static CaptureExprStandard? ReadCapture(JsonElement element)
        => element.TryGetProperty("capture", out var capture) && capture.ValueKind == JsonValueKind.Object
            ? new CaptureExprStandard(
                Int(capture, "dep"),
                Str(capture, "member"),
                Str(capture, "at") ?? "first",
                Int(capture, "index"),
                Str(capture, "path"),
                Bool(capture, "orNull") ?? false)
            : null;

    /// <summary>捕获计数节点读取（收敛 3）：<c>captureCount:{ dep, member? }</c>。</summary>
    private static CaptureCountExprStandard? ReadCaptureCount(JsonElement element)
        => element.TryGetProperty("captureCount", out var count) && count.ValueKind == JsonValueKind.Object
            ? new CaptureCountExprStandard(Int(count, "dep"), Str(count, "member"))
            : null;

    /// <summary>调用计数节点读取（收敛 3）：<c>fakeCount:{ dep, member, key? }</c>。</summary>
    private static FakeCountExprStandard? ReadFakeCount(JsonElement element)
        => element.TryGetProperty("fakeCount", out var count) && count.ValueKind == JsonValueKind.Object
            ? new FakeCountExprStandard(Int(count, "dep"), Str(count, "member"), Str(count, "key"))
            : null;

    /// <summary>
    /// 拼接段读取（W19 文本模板）：<c>concat</c> 项为表达式对象时 = 无条件段；为 <c>{ expr, when }</c> 对象时 =
    /// 条件段（<c>when</c> 求值为假即省略该段）。无 <c>concat</c> / 全为普通表达式项 → null（走既有 <c>concat</c> 口径）。
    /// </summary>
    private static IReadOnlyList<ConcatPartStandard>? ReadConcatParts(
        string portKey, JsonElement element, string context)
    {
        if (!element.TryGetProperty("concat", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return null;

        var parts = new List<ConcatPartStandard>();
        var conditional = false;

        foreach (var item in declared.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("expr", out var body))
            {
                conditional = true;
                parts.Add(new ConcatPartStandard(
                    ReadExpr(portKey, body, $"{context}.concat.expr"),
                    item.TryGetProperty("when", out var gate)
                        ? ReadExpr(portKey, gate, $"{context}.concat.when")
                        : null));
                continue;
            }

            parts.Add(new ConcatPartStandard(ReadExpr(portKey, item, $"{context}.concat")));
        }

        return conditional ? parts : null;
    }

    /// <summary>
    /// 折叠读取（W21）：<c>fold:{ over, as?, init, step:{ method, args?, rebind? } }</c>；
    /// <c>rebind</c> 缺省 **true**（累积语义：以返回值替换累积值）。
    /// </summary>
    private static ExprStandard? Fold(
        string portKey, JsonElement element, string context,
        out string? asName, out ValueSourceStandard? init, out ChainStandard? step)
    {
        asName = null;
        init = null;
        step = null;

        if (!element.TryGetProperty("fold", out var declared) || declared.ValueKind != JsonValueKind.Object)
            return null;

        if (!declared.TryGetProperty("over", out var over))
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 {context}.fold 缺 over（被遍历集合）");

        if (!declared.TryGetProperty("init", out var declaredInit) || declaredInit.ValueKind != JsonValueKind.Object)
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 {context}.fold 缺 init（初值来源）");

        init = ReadArg(portKey, declaredInit, $"{context}.fold.init");

        if (!declared.TryGetProperty("step", out var declaredStep) || declaredStep.ValueKind != JsonValueKind.Object)
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 {context}.fold 缺 step（累积调用声明）");

        var method = Str(declaredStep, "method")
            ?? throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 {context}.fold.step 缺 method");

        asName = Str(declared, "as");
        step = new ChainStandard(
            method,
            ReadArgs(portKey, declaredStep, $"{context}.fold.step '{method}'"),
            Bool(declaredStep, "rebind") ?? true);

        return ReadExpr(portKey, over, $"{context}.fold.over");
    }

    /// <summary>
    /// 聚合读取（W3）：长形 <c>{ "agg": { "over": …, "op": "sum|max|min|avg", "path": … } }</c>；
    /// 短形 <c>"agg"</c> 直给 over 时取平铺 <c>aggOp</c> / <c>aggPath</c>。缺省 op = <c>sum</c>。
    /// </summary>
    /// <summary>
    /// 聚合算子词位校验（词表唯一源 <see cref="PortImplVocabulary.AggOps"/>）：未知算子即 fail-fast，不静默回退缺省。
    /// 与结构门 enum 派生绑定构成**双保险**（结构门拦声明期、读取器拦读取期）。
    /// </summary>
    private static string? EnsureKnownAggOp(string portKey, string? op, string where)
        => PortImplVocabulary.IsKnownAggOp(op)
            ? op
            : throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 {where} 未知聚合算子 '{op}'"
                + $"（可用：{string.Join(", ", PortImplVocabulary.AggOps)}）");

    /// <summary>issue 消息口径词位校验（词表唯一源 <see cref="PortImplVocabulary.MessageModes"/>）。</summary>
    private static string? EnsureKnownMessageMode(string portKey, string? mode, string where)
        => PortImplVocabulary.IsKnownMessageMode(mode)
            ? mode
            : throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 {where} 未知消息口径 '{mode}'"
                + $"（可用：{string.Join(", ", PortImplVocabulary.MessageModes)}）");

    private static (ExprStandard? Over, string? Op, string? Path) Aggregate(
        string portKey, JsonElement owner, string context)
    {
        if (!owner.TryGetProperty("agg", out var agg)) return (null, null, null);

        if (agg.ValueKind == JsonValueKind.Object
            && !HasSource(agg)
            && agg.TryGetProperty("over", out var over))
        {
            return (
                ReadExpr(portKey, over, $"{context}.agg.over"),
                EnsureKnownAggOp(portKey, Str(agg, "op"), $"{context}.agg.op"),
                Str(agg, "path"));
        }

        return (
            ReadExpr(portKey, agg, $"{context}.agg"),
            EnsureKnownAggOp(portKey, Str(owner, "aggOp"), $"{context}.aggOp"),
            Str(owner, "aggPath"));
    }

    /// <summary>
    /// 筛选读取（W8）：长形 <c>{ "filter": { "over": …, "where": …, "path": … } }</c>；短形 <c>"filter"</c>
    /// 直给来源时取平铺 <c>filterWhere</c> / <c>filterPath</c>。缺 <c>where</c> = 保留非空元素。
    /// </summary>
    private static (ExprStandard? Over, ExprStandard? Where, string? Path) Filter(
        string portKey, JsonElement owner, string context)
    {
        if (!owner.TryGetProperty("filter", out var filter)) return (null, null, null);

        if (filter.ValueKind == JsonValueKind.Object
            && !HasSource(filter)
            && filter.TryGetProperty("over", out var over))
        {
            return (
                ReadExpr(portKey, over, $"{context}.filter.over"),
                Sub(portKey, filter, "where", $"{context}.filter"),
                Str(filter, "path"));
        }

        return (
            ReadExpr(portKey, filter, $"{context}.filter"),
            Sub(portKey, owner, "filterWhere", context),
            Str(owner, "filterPath"));
    }

    /// <summary>子表达式读取（键缺失 → null）。</summary>
    private static ExprStandard? Sub(string portKey, JsonElement owner, string name, string context)
        => owner.TryGetProperty(name, out var value) ? ReadExpr(portKey, value, $"{context}.{name}") : null;

    /// <summary>子表达式数组读取（键缺失 → null）。</summary>
    private static IReadOnlyList<ExprStandard>? ExprList(string portKey, JsonElement owner, string name, string context)
        => owner.TryGetProperty(name, out var declared) && declared.ValueKind == JsonValueKind.Array
            ? declared.EnumerateArray().Select(item => ReadExpr(portKey, item, $"{context}.{name}")).ToArray()
            : null;

    /// <summary>量词读取（<c>all</c>/<c>any</c>：<c>over</c> 遍历对象 + 判据 <c>call</c>/<c>callTarget</c>/<c>allIn</c>）。</summary>
    private static ExprStandard? Quantify(
        string portKey, JsonElement owner, string name, string context,
        out string? call, out string? callTarget, out ExprStandard? allIn)
    {
        call = null;
        callTarget = null;
        allIn = null;

        if (!owner.TryGetProperty(name, out var body) || body.ValueKind != JsonValueKind.Object) return null;

        call = Str(body, "call");
        callTarget = Str(body, "callTarget");
        allIn = body.TryGetProperty("allIn", out var bag)
            ? ReadExpr(portKey, bag, $"{context}.{name}.allIn")
            : null;

        return body.TryGetProperty("over", out var over)
            ? ReadExpr(portKey, over, $"{context}.{name}.over")
            : ReadExpr(portKey, body, $"{context}.{name}");
    }

    private static (ExprStandard? Over, ExprStandard? Where, string? Path, bool OrNull) First(
        string portKey, JsonElement element, string context)
    {
        if (!element.TryGetProperty("first", out var body) || body.ValueKind != JsonValueKind.Object)
            return (null, null, null, false);

        if (!body.TryGetProperty("over", out var over))
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 {context}.first 缺 over（被取集合）");

        return (
            ReadExpr(portKey, over, $"{context}.first.over"),
            body.TryGetProperty("where", out var where)
                ? ReadExpr(portKey, where, $"{context}.first.where")
                : null,
            Str(body, "path"),
            Bool(body, "orNull") ?? false);
    }

    /// <summary>
    /// 末命中读取（W15）：长形 <c>{ "last": { "over": …, "where": …, "path": … } }</c>；短形 <c>"last"</c> 直给来源时取
    /// 平铺 <c>lastWhere</c> / <c>lastPath</c> / <c>lastOrNull</c>。空集 / 无命中 → <c>null</c>（不抛）。
    /// </summary>
    private static (ExprStandard? Over, ExprStandard? Where, string? Path, bool OrNull) Last(
        string portKey, JsonElement element, string context)
    {
        if (!element.TryGetProperty("last", out var body) || body.ValueKind != JsonValueKind.Object)
            return (null, null, null, false);

        if (body.TryGetProperty("over", out var over))
        {
            return (
                ReadExpr(portKey, over, $"{context}.last.over"),
                Sub(portKey, body, "where", $"{context}.last"),
                Str(body, "path"),
                Bool(body, "orNull") ?? false);
        }

        return (
            ReadExpr(portKey, body, $"{context}.last"),
            Sub(portKey, element, "lastWhere", context),
            Str(element, "lastPath"),
            Bool(element, "lastOrNull") ?? false);
    }

    /// <summary>
    /// 键值表读取（W14）：长形 <c>{ "dict": { "over": …, "key": …, "value": …, "where": … } }</c>；短形 <c>"dict"</c>
    /// 直给来源时取平铺 <c>dictKey</c> / <c>dictValue</c> / <c>dictWhere</c>。缺 <c>key</c> → 求值期 fail-fast（无键不可成表）。
    /// </summary>
    private static (ExprStandard? Over, ExprStandard? Key, ExprStandard? Value, ExprStandard? Where, string? OnDuplicate) Dict(
        string portKey, JsonElement element, string context)
    {
        if (!element.TryGetProperty("dict", out var body) || body.ValueKind != JsonValueKind.Object)
            return (null, null, null, null, null);

        if (!body.TryGetProperty("over", out var over))
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 {context}.dict 缺 over（被遍历集合）");

        return (
            ReadExpr(portKey, over, $"{context}.dict.over"),
            Sub(portKey, body, "key", $"{context}.dict"),
            Sub(portKey, body, "value", $"{context}.dict"),
            Sub(portKey, body, "where", $"{context}.dict"),
            Str(body, "onDuplicate"));
    }

    /// <summary>二元算子读取（<c>[左, 右]</c> 数组或 <c>{ a, b }</c> 对象；缺失 → null）。</summary>
    private static IReadOnlyList<ExprStandard>? Pair(string portKey, JsonElement owner, string name, string context)
    {
        if (!owner.TryGetProperty(name, out var body)) return null;

        if (body.ValueKind == JsonValueKind.Array)
            return body.EnumerateArray().Select(item => ReadExpr(portKey, item, $"{context}.{name}")).ToArray();

        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("a", out var left) && body.TryGetProperty("b", out var right))
        {
            return
            [
                ReadExpr(portKey, left, $"{context}.{name}.a"),
                ReadExpr(portKey, right, $"{context}.{name}.b"),
            ];
        }

        throw new StandardViolationException(
            $"[port:standard] 端口 '{portKey}' 的 {context}.{name} 须为 [左, 右] 数组或 {{ a, b }} 对象");
    }

    /// <summary>取等算子读取（<c>countEqual: { a, b }</c>）。</summary>
    private static IReadOnlyList<ExprStandard>? CountEqual(string portKey, JsonElement element, string context)
    {
        if (!element.TryGetProperty("countEqual", out var body) || body.ValueKind != JsonValueKind.Object)
            return null;

        if (!body.TryGetProperty("a", out var left) || !body.TryGetProperty("b", out var right))
            throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 {context}.countEqual 须同时给出 a 与 b");

        return
        [
            ReadExpr(portKey, left, $"{context}.countEqual.a"),
            ReadExpr(portKey, right, $"{context}.countEqual.b"),
        ];
    }
}
