namespace Strategy.Observability.Ports;

using System.Reflection;
using System.Text.Json;
using Strategy.Observability.Diagnostics;


/// <summary>
/// 脚本求值作用域：具名槽表 + 循环变量栈（<c>foreach</c> 内步骤可见当前元素 / 下标 / 元素属性）。
/// <para>槽写入唯一入口 = <see cref="Set"/>；循环变量随迭代入栈出栈（嵌套遍历按最内层优先解析）。</para>
/// </summary>
internal sealed class EvalScope
{
    /// <summary>行表 / 词元判定的行帧名（机制内部名；声明面以 <c>from: row</c> 引用当前行）。</summary>
    internal const string RowFrame = "$row";

    private readonly Dictionary<string, object?> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tempDirs = new(StringComparer.Ordinal);
    private readonly List<(string Name, object? Element, int Index)> _loops = new();
    private readonly List<string> _notes = new();
    private string? _bottom;

    internal EvalScope(string portKey) => PortKey = portKey;

    /// <summary>端口键（诊断定位用）。</summary>
    internal string PortKey { get; }

    /// <summary>
    /// 委托工厂（W26：<c>from:delegate</c> 由求值器回调本钩子）。
    /// <para>由 <see cref="ScriptedBinding"/> 在构造作用域后注入（委托体需执行声明步骤，属执行器职责，不属取值单源）；
    /// 未注入而声明了 <c>delegate</c> 来源 → fail-fast（不静默给 null）。</para>
    /// </summary>
    internal Func<ScriptedDelegateSpec, EvalScope, IPortInput, object?[], string, object?>? Delegates { get; set; }

    /// <summary>登记临时目录（作用域名 → 绝对路径；同名重复即 fail-fast）。</summary>
    internal void SetTempDir(string name, string path)
    {
        if (!_tempDirs.TryAdd(name, path))
            throw new PortBindingViolationException(PortKey, $"端口 '{PortKey}' 的作用域名 '{name}' 重复");
    }

    /// <summary>作用域目录绝对路径（未声明 → fail-fast，不静默给空路径）。</summary>
    internal string TempDir(string? name, string context)
        => name is { Length: > 0 } && _tempDirs.TryGetValue(name, out var path)
            ? path
            : throw new PortBindingViolationException(
                PortKey, $"端口 '{PortKey}' 的 {context} 引用了未声明的临时目录作用域 '{name}'");

    /// <summary>写入槽（已存在的槽可被后续步骤覆盖：声明序即执行序）。</summary>
    internal void Set(string name, object? value) => _slots[name] = value;

    /// <summary>诊断记录（W1：⊥ 语义导致的「不执行」须留痕，不静默）。</summary>
    internal IReadOnlyList<string> Notes => _notes;

    /// <summary>追加一条诊断记录（执行期不抛，仅供诊断面读取）。</summary>
    internal void Note(string message) => _notes.Add(message);

    /// <summary>读槽（未定义 → fail-fast，不静默给 null）。</summary>
    internal object? Slot(string name, string context)
        => _slots.TryGetValue(name, out var value)
            ? value
            : throw new PortBindingViolationException(
                PortKey, $"端口 '{PortKey}' 的 {context} 引用了未定义的槽 '{name}'");

    internal bool Has(string name) => _slots.ContainsKey(name);

    /// <summary>
    /// 读槽（**⊥ 语义**，E8）：运行期未写入 → <c>⊥</c>（返回 null，**不抛**），并登记 ⊥ 出处
    /// （由最近的求值站点经 <see cref="TakeBottom"/> 消费，用于「不执行 / 不产出」的留痕）。
    /// <para>声明期拼写漂移仍由机检拦（⊥ 只覆盖运行期未定义）；步骤级槽引用（<c>foreach.slot</c> / <c>collect.from</c>）仍走严格 <see cref="Slot"/>。</para>
    /// </summary>
    internal object? SlotOrBottom(string name, string context)
    {
        if (_slots.TryGetValue(name, out var value)) return value;

        _bottom = $"槽 '{name}'（{context}）在运行期未写入";
        return null;
    }

    /// <summary>取走 ⊥ 出处（消费一次即清；无 ⊥ 时为 null）。</summary>
    internal string? TakeBottom()
    {
        var provenance = _bottom;
        _bottom = null;
        return provenance;
    }

    /// <summary>诊断记录条数（判定「本次求值是否留痕」，E8 投影字段不产出的判据之一）。</summary>
    internal int NoteCount => _notes.Count;

    /// <summary>收集槽（不存在则建）：循环结果按序追加（G1）。</summary>
    internal List<object?> Collect(string name)
    {
        if (_slots.TryGetValue(name, out var existing) && existing is List<object?> list) return list;

        var created = new List<object?>();
        _slots[name] = created;
        return created;
    }

    /// <summary>入栈循环变量（返回后须配 <see cref="Pop"/>）。</summary>
    internal void Push(string name, object? element, int index) => _loops.Add((name, element, index));

    /// <summary>出栈循环变量。</summary>
    internal void Pop() => _loops.RemoveAt(_loops.Count - 1);

    /// <summary>遍历变量解析：元素本身 / 元素下标 / 元素属性路径（未在遍历中 → fail-fast）。</summary>
    internal object? Item(string? name, int? index, string? path, string context)
    {
        var frame = _loops.LastOrDefault(loop => name is null || string.Equals(loop.Name, name, StringComparison.Ordinal));
        if (frame.Name is null)
            throw new PortBindingViolationException(
                PortKey, $"端口 '{PortKey}' 的 {context} 引用了未在遍历中的变量 '{name}'");

        var value = index is { } position ? ElementAt(frame.Element, position, context) : frame.Element;
        return path is { Length: > 0 } ? ScriptedValue.PathOf(value, path) : value;
    }

    /// <summary>
    /// 元素下标取值（索引语义单源 = <see cref="PortValues.TryElementAt"/>）：**越界 → null**（与 <c>path</c> 的 <c>[n]</c> 段、
    /// 实参 <c>optional</c> 同口径，E22-44 固化）；非序载体（JSON 对象 / 数字 / 布尔） → fail-fast（声明错误可见）。
    /// </summary>
    private object? ElementAt(object? element, int position, string context)
    {
        if (position < 0)
            throw new PortBindingViolationException(
                PortKey, $"端口 '{PortKey}' 的 {context} 的下标 {position} 非法（须 ≥ 0）");

        if (element is null) return null;
        if (PortValues.TryElementAt(element, position, out var indexed)) return indexed;
        if (PortValues.IsIndexable(element)) return null;   // 可索引但越界 → null（与 path 的 [n] 段同口径）

        if (element is JsonElement json)
            throw new PortBindingViolationException(
                PortKey,
                $"端口 '{PortKey}' 的 {context} 对 JSON {json.ValueKind} 取下标 {position}（仅数组 / 字符串可索引）");

        throw new PortBindingViolationException(
            PortKey,
            $"端口 '{PortKey}' 的 {context} 对 {element.GetType().Name} 取下标 {position}（不可索引载体）");
    }
}
