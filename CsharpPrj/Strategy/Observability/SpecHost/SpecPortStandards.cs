using DataCenter.Protocol.Standards;

namespace Strategy.Observability.SpecHost;

/// <summary>绑定方式（标准目录 <c>ports[x].impl.kind</c> 的取值域；与产品 <c>impl</c> 词表同一套）。</summary>
public enum SpecPortBindingKind
{
    /// <summary>反射绑定：<c>impl{target,method,args}</c> 声明驱动（值域型：产品 public static 直连）。</summary>
    Reflective,

    /// <summary>组合绑定：<c>impl{calls,project}</c> 声明驱动（编排型：多调用 + 投影，零夹具代码）。</summary>
    Composed,

    /// <summary>脚本绑定：<c>impl{bind,deps,script,project,deliver}</c> 声明驱动（遍历 + 结果引用 + 一般取值 + 载体）。</summary>
    Scripted,

    /// <summary>委派绑定：<c>impl{id}</c> 索引到实现字典（内联型；旧测试侧 <c>inline</c> 归一于此）。</summary>
    Delegate,

    /// <summary>冻结：只登记不绑定（结构断言迁守卫面）。</summary>
    Frozen,
}

/// <summary>
/// 测试专用端口标准读取口（执行面宿主 → **薄适配**）：解析与校验唯一归标准装载层
/// （<c>StandardCatalog</c>），本件只做「已装载标准 → 宿主驱动视图」的映射，**不触碰 JSON 解析**。
/// <para>
/// 分工（D5 归位）：装载源 = <see cref="PortStandardLoader"/> 注入点（<c>json → 端口键 → PortStandard</c>）；
/// 本件只按键检索，故宿主不自行解析台账 JSON（避免第二份解析口径）。
/// </para>
/// <para>零领域词汇：端口键与实现 id 均为不透明字符串。</para>
/// </summary>
public sealed class SpecPortStandards
{
    private readonly Dictionary<string, PortStandard> _byKey;
    private readonly IReadOnlyList<PortStandard> _entries;

    /// <summary>
    /// 标准装载注入点：<c>JSON 文本 → 端口键（Ordinal）→ 已装载标准</c>。端口键的唯一真源 =
    /// <c>StandardCatalog.Load(json).Ports[i].Key</c>（本件不猜键、不解析 JSON）。
    /// </summary>
    public static PortStandardLoader Projection { get; set; } = DefaultProjection;

    private static IReadOnlyDictionary<string, PortStandard> DefaultProjection(string json)
        => throw new InvalidOperationException(
            "[port:standard] 标准装载未装配：须在装配处注入 SpecPortStandards.Projection" +
            "（解析与校验唯一归标准装载层，宿主不自行解析台账 JSON）");

    private SpecPortStandards(Dictionary<string, PortStandard> byKey, IReadOnlyList<PortStandard> entries, string json)
    {
        _byKey = byKey;
        _entries = entries;
        Json = json;
    }

    /// <summary>原始 JSON（供内核契约播种：单一真源不复制）。</summary>
    public string Json { get; }

    /// <summary>端口键（Ordinal 升序）。</summary>
    public IReadOnlyList<string> PortKeys => _entries.Select(s => s.Key).ToArray();

    /// <summary>全部标准条目（端口键 Ordinal 升序）。</summary>
    public IReadOnlyList<PortStandard> Entries => _entries;

    /// <summary>按键取标准条目。</summary>
    public bool TryGet(string portKey, out PortStandard standard) => _byKey.TryGetValue(portKey, out standard!);

    /// <summary>取条目或抛出（含已登记清单：防拼写漂移静默）。</summary>
    public PortStandard Require(string portKey)
        => TryGet(portKey, out var standard)
            ? standard
            : throw new InvalidOperationException(
                $"[port:standard] 端口 '{portKey}' 未登记。已登记：{string.Join(", ", PortKeys)}");

    /// <summary>按 JSON 文本装载（装载源 = 注入的标准装载器；本件只索引，不解析）。</summary>
    public static SpecPortStandards Parse(string json)
    {
        PortGuard.Required(json, nameof(json));

        var map = new Dictionary<string, PortStandard>(StringComparer.Ordinal);
        foreach (var (portKey, standard) in Projection(json))
            if (!map.TryAdd(portKey, standard))
                throw new InvalidOperationException(
                    $"[port:standard] 端口 '{portKey}' 重复登记（单一实现点被破坏）");

        var entries = map.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => map[k]).ToArray();
        return new SpecPortStandards(map, entries, json);
    }

    /// <summary>
    /// 种类字符串 → 上层枚举（标准目录已归一；未知即抛，属实现漂移）。
    /// <para>**缺省已取消**：<c>impl</c> 缺失**不**再隐式按 <see cref="SpecPortBindingKind.Delegate"/> 处理
    /// （装载期 <c>impl.kind</c> 必填，缺即已抛）；此处缺失 ⇒ 上游实现漂移，当场抛出（禁静默放行）。</para>
    /// </summary>
    public static SpecPortBindingKind KindOf(PortStandard standard)
    {
        PortGuard.Required(standard, nameof(standard));

        return standard.Impl is { } impl
            ? ParseKind(standard.Key, impl.Kind)
            : throw new InvalidOperationException(
                $"[port:standard] 端口 '{standard.Key}' 缺 impl（缺省已取消：不得再隐式按 delegate 处理）");
    }

    /// <summary>种类字符串 → 上层枚举（标准目录已归一；未知即抛，属实现漂移）。</summary>
    public static SpecPortBindingKind ParseKind(string portKey, string kind) => kind switch
    {
        "reflective" => SpecPortBindingKind.Reflective,
        "composed" => SpecPortBindingKind.Composed,
        "scripted" => SpecPortBindingKind.Scripted,
        "frozen" => SpecPortBindingKind.Frozen,

        // SK-28：遗留值（只读兼容）——新增禁止。
        "delegate" => SpecPortBindingKind.Delegate,
        _ => throw new InvalidOperationException(
            $"[port:standard] 端口 '{portKey}' 的 impl.kind '{kind}' 非法（标准目录归一后不应出现未知种类；"
            + "允许值：reflective|composed|scripted|frozen；遗留值：delegate）"),
    };
}
