namespace Modules.Compute.Formula;

/// <summary>图上节点的只读视图。</summary>
public readonly record struct GraphNode(
    int Id,
    OperatorSpec Spec,
    int[] Inputs,
    string Key,
    IReadOnlyList<int> Consumers);

/// <summary>
/// 算子图（DAG）：节点只增不改，输入必须是已存在的节点 ⇒ <b>节点 Id 即拓扑序</b>。
/// <para>结构键（CSE）：<c>Kind + Shape + Tile + 参数签名 + 输出张量名 + 输入键</c>；
/// 仅当算子声明 <see cref="OperatorSpec.Commutative"/> 时输入键才排序（避免误合并 <c>a-b</c>/<c>b-a</c>）。</para>
/// </summary>
public sealed class OperatorGraph
{
    private sealed class Entry
    {
        public required int Id;
        public required OperatorSpec Spec;
        public required int[] Inputs;
        public required string Key;
        public readonly List<int> Consumers = new();
    }

    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, int> _cse = new();

    public int Count => _entries.Count;

    public GraphNode this[int id]
    {
        get
        {
            var entry = EntryAt(id);
            return new GraphNode(entry.Id, entry.Spec, entry.Inputs, entry.Key, entry.Consumers);
        }
    }

    /// <summary>添加无输入节点（常量/参数）。</summary>
    public int Add(OperatorSpec spec) => Add(spec, ReadOnlySpan<int>.Empty);

    /// <summary>添加节点；结构键命中已有节点时直接复用（CSE）。</summary>
    public int Add(OperatorSpec spec, ReadOnlySpan<int> inputs)
    {
        ArgumentNullException.ThrowIfNull(spec);

        for (int i = 0; i < inputs.Length; i++)
        {
            if ((uint)inputs[i] >= (uint)_entries.Count)
                throw new ArgumentOutOfRangeException(nameof(inputs), $"输入节点 {inputs[i]} 不存在");
            if (inputs[i] >= _entries.Count)
                throw new InvalidOperationException("输入节点必须是已添加的节点（保证 Id 拓扑序）");
        }

        string key = BuildKey(spec, inputs);

        if (_cse.TryGetValue(key, out int existing))
            return existing;

        int id = _entries.Count;
        var entry = new Entry
        {
            Id = id,
            Spec = spec,
            Inputs = inputs.ToArray(),
            Key = key,
        };

        foreach (int input in entry.Inputs)
            _entries[input].Consumers.Add(id);

        _entries.Add(entry);
        _cse[key] = id;
        return id;
    }

    /// <summary>按输出张量名查找唯一生产节点；不存在返回 -1。</summary>
    public int ProducerOf(TensorKey tensor)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Spec.Output == tensor)
                return i;
        }

        return -1;
    }

    public string KeyOf(int id) => EntryAt(id).Key;

    private Entry EntryAt(int id)
    {
        if ((uint)id >= (uint)_entries.Count)
            throw new ArgumentOutOfRangeException(nameof(id), $"节点 {id} 不存在");
        return _entries[id];
    }

    private string BuildKey(OperatorSpec spec, ReadOnlySpan<int> inputs)
    {
        string[] inputKeys = new string[inputs.Length];
        for (int i = 0; i < inputs.Length; i++)
            inputKeys[i] = _entries[inputs[i]].Key;

        if (spec.Commutative && inputKeys.Length > 1)
            Array.Sort(inputKeys, StringComparer.Ordinal);

        string inputPart = string.Join(",", inputKeys);

        // \u0001 分隔避免 "a|b" 与 "ab" 拼接歧义
        return string.Join(
            "\u0001",
            spec.Kind,
            spec.Shape.ToString(),
            spec.Tile.ToString(),
            spec.ParameterSignature.ToString(),
            spec.Output.Name,
            inputPart);
    }
}
