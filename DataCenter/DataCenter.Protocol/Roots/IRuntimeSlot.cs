namespace DataCenter.Protocol.Roots;

/// <summary>
/// 运行时槽分区宿主（DF8 根分区，P10）—— 根的另一半：状态真源承载"可序列化的值"，本端口承载
/// "不可序列化的进程内对象"（网关实例 / 缓存 / 编排产物 / 句柄持有者）。
/// <para>
/// 纪律：① 对象槽是<b>根的分区</b>而非第二根（禁止各自静态持有，否则换代必然漏迁）；
/// ② 按<b>域键</b>细分（一个逻辑域一个分区），不逐对象开槽（防退化为服务定位器）；
/// ③ 装配期注册、运行期只读；处置期由宿主统一释放。
/// </para>
/// 权威: DF8/P10（单根持有 + 根分区）；接入侧设计见 Frame\DataCenter\Doc 的运行时数据接入文档 §2.3。
/// </summary>
public interface IRuntimeSlot
{
    /// <summary>已注册的分区域键集（诊断/装配期可枚举）。</summary>
    IReadOnlyCollection<string> Domains { get; }

    /// <summary>取分区（不存在则创建空分区；分区本身是"域键命名空间"，不是对象）。</summary>
    IRuntimeSlotPartition Partition(string domainKey);
}

/// <summary>
/// 运行时槽分区（按域键细分）—— 域对象的装配期注册表 + 运行期只读解析面。
/// 注册语义：同键同实例 = 幂等；同键不同实例 = fail-fast（禁止"第二份运行时/第二根"）。
/// </summary>
public interface IRuntimeSlotPartition
{
    /// <summary>分区键（域键，由域扩展在装配期登记；一域一分区，禁止逐对象开槽）。</summary>
    string DomainKey { get; }

    /// <summary>已注册的槽键集（诊断用；不含值）。</summary>
    IReadOnlyCollection<string> Keys { get; }

    /// <summary>注册槽对象（装配期单点写入；重复注册不同实例 = fail-fast）。</summary>
    void Register(string key, object value);

    /// <summary>
    /// 替换槽对象（<b>处置期写口</b>：换代重装配 / 重装配覆盖；同实例幂等）。
    /// 旧值处置由调用方负责（槽宿主不越权释放仍未解绑的对象）。
    /// </summary>
    void Replace(string key, object value);

    /// <summary>尝试解析槽对象（运行期只读路径；类型不符 = 未命中，不抛错）。</summary>
    bool TryGet<T>(string key, out T? value) where T : class;

    /// <summary>解析槽对象；未注册或类型不符 → <see cref="KeyNotFoundException"/>（fail-fast，不给静默 null）。</summary>
    T Get<T>(string key) where T : class;

    /// <summary>解析槽对象（无类型约束版；供非泛型装配诊断路径使用）。</summary>
    object Get(string key);
}
