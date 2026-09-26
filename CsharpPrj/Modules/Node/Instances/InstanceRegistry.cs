using Modules.Node.Diagnostics;

namespace Modules.Node.Instances;

/// <summary>
/// 实例标识（G-3）：<c>Kind + Key</c> 复合身份；值语义，可作字典键。
/// <para>文本形态 = <c>{kind}:{key}</c>（唯一拼法，禁第二套拼法；承载定位亦以此键反解，I1）。</para>
/// </summary>
public readonly record struct InstanceId
{
    private InstanceId(string kind, string key)
    {
        Kind = kind;
        Key = key;
    }

    /// <summary>实例类型（<see cref="InstanceKindSpec.Kind"/>）。</summary>
    public string Kind { get; }

    /// <summary>实例键（Kind 内唯一）。</summary>
    public string Key { get; }

    /// <summary>是否已赋值（默认结构 = 未赋值；用于诊断/防御）。</summary>
    public bool IsSet => !string.IsNullOrEmpty(Kind) && !string.IsNullOrEmpty(Key);

    /// <summary>构造（Kind/Key 均须非空；空即抛，不给静默空身份）。</summary>
    public static InstanceId Create(string kind, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new InstanceId(kind, key);
    }

    /// <summary>文本形态（唯一拼法）。</summary>
    public override string ToString() => $"{Kind}:{Key}";

    /// <summary>反解文本形态（首处 <c>:</c> 之前为 Kind；缺失/空段 ⇒ false，不抛）。</summary>
    public static bool TryParse(string? text, out InstanceId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var index = text.IndexOf(':', StringComparison.Ordinal);
        if (index <= 0 || index == text.Length - 1)
            return false;

        var kind = text[..index];
        var key = text[(index + 1)..];
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(key))
            return false;

        id = new InstanceId(kind, key);
        return true;
    }
}

/// <summary>实例句柄（G-3）：身份 + 承载 + 父（owns 侧）。同身份必同承载（I2：禁第二承载）。</summary>
/// <param name="Id">实例身份。</param>
/// <param name="Store">承载（一实例一 Store 句柄）。</param>
/// <param name="ParentId">父实例文本键（<see cref="InstanceId.ToString"/>；顶层 ⇒ null）。</param>
public sealed record InstanceHandle(InstanceId Id, IInstanceStore Store, string? ParentId = null)
{
    /// <summary>承载阶段（透出，便于读侧判据不直接摸 Store）。</summary>
    public InstanceStage Stage => Store.Stage;
}

/// <summary>
/// 进程级唯一索引（G-3）：<c>InstanceId → InstanceHandle</c>；**装配期写入、运行期只读**；由宿主**注入**（禁进程静态，I7）。
/// <para>
/// 治本判据（§0）：<see cref="RegisterAsync"/> 内「打开承载 → 登记索引」顺序完成，
/// 故「实例创建 = Store 打开 = 承载确定」在同一装配相位内原子发生 —— 结构上排除「先写入、后确定承载」。
/// </para>
/// </summary>
public sealed class InstanceRegistry
{
    private readonly Dictionary<InstanceId, InstanceHandle> _handles = new();
    private readonly INodeDiagnosticsSink _diagnostics;
    private readonly object _sync = new();
    private bool _frozen;

    public InstanceRegistry(INodeDiagnosticsSink? diagnostics = null)
        => _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;

    /// <summary>是否已冻结（运行期只读门）。</summary>
    public bool IsFrozen
    {
        get
        {
            lock (_sync)
            {
                return _frozen;
            }
        }
    }

    /// <summary>已登记实例数。</summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _handles.Count;
            }
        }
    }

    /// <summary>全部句柄（序 = 身份文本序数序，稳定可比）。</summary>
    public IReadOnlyList<InstanceHandle> All
    {
        get
        {
            lock (_sync)
            {
                return _handles.Values.OrderBy(static h => h.Id.ToString(), StringComparer.Ordinal).ToArray();
            }
        }
    }

    /// <summary>冻结索引（装配相位末调用一次；此后注册被拒）。</summary>
    public void Freeze()
    {
        lock (_sync)
        {
            if (_frozen)
                return;

            _frozen = true;
        }

        _diagnostics.Report(new NodeDiagnostic(
            "instance.registry-frozen",
            NodeDiagnosticSeverity.Info,
            "实例索引已冻结（装配期写入、运行期只读）",
            "instance"));
    }

    /// <summary>
    /// 登记实例（装配相位）：**先打开承载、再登记索引**（任一步失败不留半状态）。
    /// 冻结后 / 同身份重复 / 承载已封存 ⇒ 抛（禁第二承载，I2/I3）。
    /// </summary>
    public async Task<InstanceHandle> RegisterAsync(
        InstanceId id, IInstanceStore store, string? parentId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!id.IsSet)
            throw new ArgumentException("实例身份未赋值（Kind/Key 均须非空）", nameof(id));

        lock (_sync)
        {
            if (_frozen)
                throw new InvalidOperationException($"实例索引已冻结，禁止登记 '{id}'（装配期写入、运行期只读，I7）。");

            if (_handles.ContainsKey(id))
                throw new InvalidOperationException(
                    $"实例 '{id}' 已登记承载 '{_handles[id].Store.StoreKey}'，禁止第二承载（I2）。");
        }

        // 承载先确定（打开）再登记：结构上排除「先写入、后确定承载」（§0 治本判据 / I4）。
        await store.OpenAsync(id.ToString(), cancellationToken).ConfigureAwait(false);

        var handle = new InstanceHandle(id, store, parentId);
        lock (_sync)
        {
            if (!_handles.TryAdd(id, handle))
                throw new InvalidOperationException($"实例 '{id}' 并发重复登记（禁第二承载，I2）。");
        }

        _diagnostics.Report(new NodeDiagnostic(
            "instance.registered",
            NodeDiagnosticSeverity.Info,
            $"实例已登记（承载 {store.StoreKey} / 阶段 {store.Stage}）",
            $"{id}|{store.Path}"));

        return handle;
    }

    /// <summary>尝试取句柄（未登记 ⇒ false，调用方负责可见化）。</summary>
    public bool TryGet(InstanceId id, out InstanceHandle? handle)
    {
        lock (_sync)
        {
            return _handles.TryGetValue(id, out handle);
        }
    }

    /// <summary>取句柄；未登记 ⇒ 抛（缺实例不静默造）。</summary>
    public InstanceHandle Require(InstanceId id)
        => TryGet(id, out var handle)
            ? handle!
            : throw new KeyNotFoundException($"未登记的实例 '{id}'（装配点缺失即缺陷，禁静默造实例）。");

    /// <summary>直接子实例（owns 侧：父键匹配；序 = 身份文本序数序）。</summary>
    public IReadOnlyList<InstanceHandle> ChildrenOf(InstanceId parent)
    {
        var key = parent.ToString();
        lock (_sync)
        {
            return _handles.Values
                .Where(h => string.Equals(h.ParentId, key, StringComparison.Ordinal))
                .OrderBy(static h => h.Id.ToString(), StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <summary>
    /// 级联目标（I3 / G-4）：父处置时按父 Kind 声明的 <c>Holds</c> 规则递归取子集；
    /// <c>Refs</c>/<c>Derived</c> 不级联（边仅为只读关系，多对多保留为边）。
    /// </summary>
    public IReadOnlyList<InstanceId> CascadeTargets(InstanceId parent, InstanceKindRegistry kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);

        var targets = new List<InstanceId>();
        var visited = new HashSet<InstanceId> { parent };
        var pending = new Stack<InstanceId>();
        pending.Push(parent);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var heldKinds = HoldsKindsOf(current.Kind, kinds);
            foreach (var child in ChildrenOf(current))
            {
                if (!visited.Add(child.Id))
                    continue;

                if (!heldKinds.Contains(child.Id.Kind))
                    continue;   // Refs / Derived / 未声明 ⇒ 不级联

                targets.Add(child.Id);
                pending.Push(child.Id);
            }
        }

        return targets;
    }

    /// <summary>释放单个实例承载并从索引移除（不递归级联；级联由调用方按 <see cref="CascadeTargets"/> 驱动）。</summary>
    public async Task<bool> DisposeAsync(InstanceId id, CancellationToken cancellationToken = default)
    {
        InstanceHandle handle;
        lock (_sync)
        {
            if (!_handles.Remove(id, out var found))
                return false;

            handle = found;
        }

        await handle.Store.DisposeAsync().ConfigureAwait(false);
        _diagnostics.Report(new NodeDiagnostic(
            "instance.disposed",
            NodeDiagnosticSeverity.Info,
            $"实例承载已释放（{handle.Store.StoreKey}）",
            id.ToString()));
        return true;
    }

    /// <summary>父 Kind 声明的 <c>Holds</c> 子 Kind 集（未声明 ⇒ 空集 + 可见化：级联取安全侧）。</summary>
    private HashSet<string> HoldsKindsOf(string kind, InstanceKindRegistry kinds)
    {
        if (kinds.TryGet(kind, out var spec) && spec is not null)
        {
            return new HashSet<string>(
                spec.Cascades.Where(static rule => rule.CascadesOnDispose).Select(static rule => rule.Kind),
                StringComparer.Ordinal);
        }

        _diagnostics.Report(new NodeDiagnostic(
            "instance.cascade-kind-unknown",
            NodeDiagnosticSeverity.Warning,
            "实例 Kind 未声明，级联裁决取不级联（安全侧）",
            kind));
        return new HashSet<string>(StringComparer.Ordinal);
    }
}
