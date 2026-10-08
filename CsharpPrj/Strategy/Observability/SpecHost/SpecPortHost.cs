using DataCenter.Protocol.Standards;
using Strategy.Observability.Diagnostics;
using Strategy.Observability.Ports;
using Strategy.Observability.Projection;

namespace Strategy.Observability.SpecHost;

/// <summary>
/// 端口宿主（执行面通用端口驱动）：持端口目录（契约面 + 调用记录）与端口面投影，
/// 把**域无关请求体**（<see cref="PortInvocationRequest"/>）驱动为端口调用，
/// 并把四个投影根求值为可反射取值对象。
/// <para>
/// 归位批 I2（宿主上移 + DTO 退场）：本类实现契约面 <see cref="IPortHost"/>，**零测试框架依赖**
/// （用例步骤上下文 → 请求体的翻译、内核载体 → 测试内核载体（<c>SpecIssues</c>/<c>SpecAction</c>）的转译、
/// 逐行替身装配全部由测试侧**薄适配器**承担）。
/// </para>
/// <para>
/// 参数直传：位置实参 = 用例 <c>args</c> 的插值文本；具名实参 = 用例 <c>with</c> 的原始 JSON
/// （由绑定器按目标形参类型反序列化）；源文本 = 用例 <c>text</c>/<c>file</c>。宿主不感知这些细节。
/// </para>
/// <para>
/// 领域接缝（全部注入，本类零领域引用）：环境作用域 <see cref="ISpecScopeProvider"/>、
/// 观测事件源 <see cref="ISpecEventSource"/>、端口面 <see cref="ISpecPortSurface"/>。
/// </para>
/// </summary>
public sealed class SpecPortHost : IPortHost, IDisposable
{
    /// <summary>无记账来源时的空视图（键 Ordinal 序稳定）。</summary>
    private static readonly IReadOnlyDictionary<string, string> NoEntries =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly IPortCatalog _catalog;
    private readonly ISpecPortSurface _surface;
    private readonly PortComposer? _composer;
    private readonly ISpecScopeProvider? _scope;
    private readonly ISpecEventSource? _events;
    private readonly HashSet<string> _replaced = new(StringComparer.Ordinal);
    private readonly List<string> _fieldIssues = new();
    private bool _disposed;

    /// <summary>
    /// <paramref name="composer"/>：装配期记账来源（内核唯一权威；null = 未装配，Unbound/Frozen 为空视图）；
    /// <paramref name="scope"/> / <paramref name="events"/>：领域侧注入面（null = 无作用域 / 仅 <c>Port(…)</c> 根）。
    /// </summary>
    public SpecPortHost(
        IPortCatalog catalog,
        ISpecPortSurface surface,
        PortComposer? composer = null,
        ISpecScopeProvider? scope = null,
        ISpecEventSource? events = null)
    {
        _catalog = PortGuard.Required(catalog, nameof(catalog));
        _surface = PortGuard.Required(surface, nameof(surface));
        _composer = composer;
        _scope = scope;
        _events = events;
    }

    /// <summary>端口目录（调用记录 / 解析入口；守卫与诊断用）。</summary>
    public IPortCatalog Catalog => _catalog;

    /// <summary>端口面投影（键集 / 接管判定 / 产物字段 / 内联实现面）。</summary>
    public ISpecPortSurface Surface => _surface;

    /// <summary>冻结（只登记未绑定）端口键 → 原因（记账唯一权威 = 内核 <see cref="PortComposer"/>）。</summary>
    public IReadOnlyDictionary<string, string> Frozen => _composer?.Frozen ?? NoEntries;

    /// <summary>绑定失败（fail-safe 记录）端口键 → 原因（记账唯一权威 = 内核 <see cref="PortComposer"/>）。</summary>
    public IReadOnlyDictionary<string, string> Unbound => _composer?.Unbound ?? NoEntries;

    /// <summary>已登记内联实现端口键（Ordinal 升序）：端口面声明 <c>binding: inline</c> 者的实现面（守卫对账用）。</summary>
    public IReadOnlyList<string> InlinePortKeys => _surface.InlinePortKeys;

    /// <summary>已播种端口契约键（Ordinal 升序）：声明面（含只登记未绑定者），守卫验证契约并集用。</summary>
    public IReadOnlyList<string> ContractKeys => _surface.ContractKeys;

    /// <summary>已登记端口键（Ordinal 升序；= 路由面）。</summary>
    public IReadOnlyList<string> PortKeys => _surface.PortKeys;

    /// <summary>
    /// 产物字段契约诊断（F3）：复合产物（具名映射）产出的键超出其边界契约声明字段集的条目。
    /// 只收集不阻断（字段面补齐前不打断基线）；行级隔离随 <see cref="Reset"/> 清空。
    /// </summary>
    public IReadOnlyList<string> FieldContractIssues => _fieldIssues;

    /// <summary>是否接管该端口键（未登记 → false，交回既有路径）。</summary>
    public bool Handles(string portKey) => _surface.Handles(portKey);

    /// <summary>声明端口产物字段（<c>|</c> 分隔；未声明 → false = 弱校验）。</summary>
    public bool TryGetProductFields(string portKey, out string productFields)
        => _surface.TryGetProductFields(portKey, out productFields);

    /// <summary>行级隔离：清空调用记录与产物字段诊断（行内断言只应看到本行调用）。</summary>
    public void Reset()
    {
        _catalog.Log.Clear();
        _fieldIssues.Clear();
    }

    /// <summary>
    /// 逐行替身注入（P4，由**接线半**在每行用例执行前调用）：替换端口的当前绑定并记账，
    /// 供下一次 <see cref="RestoreReplaced"/> 回填（逐行隔离，<c>rows</c> 多行各自独立）。
    /// <para>未登记端口键 → 抛（<see cref="PortNotRegisteredException"/>，含能力清单，防拼写漂移静默）。</para>
    /// </summary>
    public void ReplaceBinding(string portKey, IPortBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portKey);
        PortGuard.Required(binding, nameof(binding));

        var canonical = ResolveStep(portKey);   // given ports 可写步骤名（历史 kind 名）→ 归一到实现承载位
        _catalog.Replace(canonical, binding);
        _replaced.Add(canonical);
    }

    /// <summary>回填上一行被替换的端口（逐行隔离；端口面无装配期工厂 → 跳过，不伪造）。</summary>
    public void RestoreReplaced()
    {
        foreach (var portKey in _replaced)
        {
            var factory = _surface.BindingFactory(portKey);
            if (factory is null) continue;   // 冻结 / 未登记：本就不在可替换面
            _catalog.Replace(portKey, factory());
        }

        _replaced.Clear();
    }

    /// <summary>
    /// 调用端口（<see cref="IPortHost.Invoke"/>）：请求体直传（宿主不手写编码解析）；
    /// 冻结 / 未绑定即抛可定位异常；端口自身异常统一带端口键归因。
    /// <para>返回值即绑定产物原文（内核载体如 <c>IPortIssues</c>/<c>IDeferredAction</c> 的转译归接线半）。</para>
    /// </summary>
    public object? Invoke(PortInvocationRequest request)
    {
        PortGuard.Required(request, nameof(request));

        // 内联 invocation（S2）：步骤自带标准片段 ⇒ 现场装载 + 编译 + 求值，不经目录（存量按键路径零回归）。
        if (request.InlineStandardJson is { Length: > 0 } inlineJson)
            return InvokeInline(inlineJson, request);

        var portKey = ResolveStep(request.PortKey);

        if (Frozen.TryGetValue(portKey, out var frozenReason))
            throw new PortBindingViolationException(portKey, $"端口已冻结（只登记未绑定）：{frozenReason}");

        if (Unbound.TryGetValue(portKey, out var unboundReason))
            throw new PortBindingViolationException(portKey, $"端口未绑定：{unboundReason}");

        try
        {
            var result = _catalog.Resolve(portKey).Binding
                .InvokeAsync(BuildInput(request, portKey)).AsTask().GetAwaiter().GetResult();
            var normalized = PortProductNormalizer.Normalize(result, DeclaredProductFields(portKey));
            InspectProduct(portKey, normalized);
            return normalized;
        }
        catch (Exception ex) when (ex is not ObservabilityException)
        {
            // 端口自身异常统一带端口键归因（调用记录已按 errorClass 记一条，见 CallLog）。
            throw new PortBindingViolationException(portKey, $"端口调用失败：{ex.GetType().Name}：{ex.Message}");
        }
    }

    /// <summary>
    /// 内联 invocation 执行（S2 <c>when invoke</c>）：标准片段的解析与校验唯一归标准装载层
    /// （<see cref="SpecPortStandards.Projection"/> 注入点），本件只做「已装载标准 → 编译 → 求值」。
    /// <para>片段须恰含 **1** 个端口条目（多于/少于即抛，防静默取首条）；条目不可求值由
    /// <see cref="PortBinding.RequireBinding"/> 按既有诊断 fail-fast。</para>
    /// </summary>
    private object? InvokeInline(string inlineJson, PortInvocationRequest request)
    {
        var loaded = SpecPortStandards.Projection(inlineJson);
        if (loaded.Count != 1)
            throw new ObservabilityException(
                $"[spec:port] 内联 invocation 的标准片段须恰含 1 个端口条目，实得 {loaded.Count}");

        var standard = System.Linq.Enumerable.First(loaded.Values);

        try
        {
            var binding = PortBinding.FromSpec(standard.Key, PortSpecCompiler.Compile(standard)).RequireBinding();
            // 记账单一实现：经目录记录装饰（P8），使内联调用对 Port(<条目键>) 投影可见。
            var recorded = _catalog.Decorate(standard.Key, binding);
            var result = recorded.InvokeAsync(BuildInput(request, standard.Key)).AsTask().GetAwaiter().GetResult();
            return PortProductNormalizer.Normalize(result, standard.Impl?.ProductFields);
        }
        catch (Exception ex) when (ex is not ObservabilityException)
        {
            throw new PortBindingViolationException(standard.Key, $"内联端口调用失败：{ex.GetType().Name}：{ex.Message}");
        }
    }

    /// <summary>声明的产物字段串（<c>|</c> 分隔；未声明 → null = 弱校验，归一不补位）。</summary>
    private string? DeclaredProductFields(string portKey)
        => _surface.TryGetProductFields(portKey, out var fields) ? fields : null;

    /// <summary>
    /// 产物字段契约对账（F3）：仅对**具名映射**产物（<c>Product(...)</c> 复合产物）判定
    /// 「产出键 ⊆ 契约声明字段」，超出者记入诊断；非映射产物（延迟动作 / 问题清单 / 标量）不判定。
    /// </summary>
    private void InspectProduct(string portKey, object? result)
    {
        if (result is not IReadOnlyDictionary<string, object?> product || product.Count == 0) return;

        _fieldIssues.AddRange(FieldContractValidator.Validate(_catalog.Resolve(portKey).Contract, product.Keys));
    }

    /// <summary>
    /// 投影根求值：<c>Port(键)</c> 取调用记录投影；<c>Event(分类)</c> 取当前作用域内该分类的事件切片；
    /// <c>Timeline(作用域)</c> / <c>Aggregate(作用域)</c> 取该作用域的事件序与全度量求和（后三者需注入观测事件源）。
    /// </summary>
    public object? Project(string rootExpression)
    {
        var (name, argument) = ParseRoot(rootExpression);
        if (argument.Length == 0)
            throw new ObservabilityException($"[spec:port] 投影根 '{rootExpression}' 缺少参数（如 Port(<端口键>)）");

        return name.ToLowerInvariant() switch
        {
            "port" => ProjectPort(argument),
            "event" => EventProjection.Of(argument, AmbientEvents()),
            "timeline" => new TimelineProjection(argument, ReadScopeEvents(argument)),
            "aggregate" => MetricTotalsProjection.Of(ReadScopeEvents(argument)),
            _ => throw new ObservabilityException($"[spec:port] 未识别的投影根 '{rootExpression}'"),
        };
    }

    /// <summary>端口投影：参数可为端口键或步骤名（历史 kind 名），统一归一到实现承载位后取调用记录（记录键 = 端口键）。</summary>
    private Strategy.Observability.Projection.PortProjection ProjectPort(string step)
    {
        var portKey = ResolveStep(step);
        return new Strategy.Observability.Projection.PortProjection(portKey, _catalog.Log.For(portKey));
    }

    /// <summary>步骤名 → 端口键（路线 B 归一：<c>.specs</c> 步骤名是冻结 ID，台账键是实现承载位）；不可解析即原样返回，交回既有诊断。</summary>
    private string ResolveStep(string step)
        => _surface.TryResolveStep(step, out var portKey) ? portKey : step;

    /// <summary>释放注入的观测事件源（U11：释放归注入源；未注入即无操作）。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _events?.Dispose();
    }

    /// <summary>当前作用域（无环境作用域 → 抛「run/round 未进入」；不伪造参数，G-5）。</summary>
    private IReadOnlyList<ObservationOutcome> AmbientEvents()
    {
        var scopeId = _scope?.AmbientScopeId()
            ?? throw new ObservabilityException(
                "[spec:port] 投影根 Event(...) 需要当前作用域，但无环境作用域（run/round 未进入）");

        return ReadScopeEvents(scopeId);
    }

    /// <summary>观测事件读取（未装配观测 → 抛可定位异常，不得静默空集；读前排空由事件源实现承担，U9）。</summary>
    private IReadOnlyList<ObservationOutcome> ReadScopeEvents(string scopeId)
    {
        if (_events is null || !_events.IsAvailable)
            throw new ObservabilityException(
                "[spec:port] Event/Timeline/Aggregate 投影根需要观测事件源，但本工程未装配观测："
                + "请在装配处注入 ISpecEventSource（或改用 Port(…) 根）。");

        return _events.Read(scopeId);
    }

    /// <summary>解析根表达式（<c>Name(arg)</c>；参数两端引号/空白去除）。</summary>
    private static (string Name, string Argument) ParseRoot(string rootExpression)
    {
        var open = rootExpression.IndexOf('(');
        var close = rootExpression.LastIndexOf(')');
        if (open <= 0 || close <= open)
            throw new ObservabilityException($"[spec:port] 投影根 '{rootExpression}' 形态非法（应为 Name(参数)）");

        var argument = rootExpression[(open + 1)..close].Trim();
        if (argument.Length >= 2 && ((argument[0] == '\'' && argument[^1] == '\'') || (argument[0] == '"' && argument[^1] == '"')))
            argument = argument[1..^1];

        return (rootExpression[..open].Trim(), argument);
    }

    /// <summary>
    /// 请求体 → 内核端口输入（**直传**：位置实参 / 具名实参 / 源文本 / 来源锚一一对应，
    /// 出参不做二次解析——翻译责任在接线半的入参侧）。
    /// </summary>
    private static PortInput BuildInput(PortInvocationRequest request, string portKey)
        => new(portKey, request.Args, request.With, request.Text, request.Src);
}

