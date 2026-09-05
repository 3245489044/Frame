#nullable enable

using System.Windows.Forms;

namespace Share.ControlLookup;

/// <summary>
/// 控件查找结果（强类型返回：命中实参 + 命中源，供诊断/审计）。
/// </summary>
public readonly struct ControlLookupResult
{
    public static ControlLookupResult Miss => default;

    public ControlLookupResult(Control? control, IControlLookupSource? source)
    {
        Control = control;
        Source = source;
    }

    public Control? Control { get; }

    /// <summary>命中的源；未命中（Control==null）时为 null。</summary>
    public IControlLookupSource? Source { get; }

    public bool IsHit => Control != null;
}

/// <summary>
/// 控件查找聚合器（ControlLookupRegistry）——唯一查找内核。
/// 按 <see cref="IControlLookupSource.Priority"/> 升序遍历已注册源，首个命中即返回（有序权威链），
/// 替代既有 finder 族群手写的「ControlPool → EntityTree → Registry → 控件树」多层 fallback。
/// </summary>
public sealed class ControlLookupRegistry
{
    private readonly List<IControlLookupSource> _sources = new();
    private bool _dirty = true;

    /// <summary>注册一个查找源（按 Priority 升序维护；同名源去重覆盖）。</summary>
    public void AddSource(IControlLookupSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var existingIndex = _sources.FindIndex(s => string.Equals(s.Name, source.Name, StringComparison.Ordinal));
        if (existingIndex >= 0)
            _sources[existingIndex] = source;
        else
            _sources.Add(source);

        _dirty = true;
    }

    public bool RemoveSource(string name)
    {
        var removed = _sources.RemoveAll(s => string.Equals(s.Name, name, StringComparison.Ordinal)) > 0;
        if (removed)
            _dirty = true;
        return removed;
    }

    public void ClearSources()
    {
        _sources.Clear();
        _dirty = true;
    }

    /// <summary>已按 Priority 升序排好的只读源列表（每次访问重排/快照，保证线程安全读取）。</summary>
    public IReadOnlyList<IControlLookupSource> Sources => GetOrderedSnapshot();

    public int SourceCount => _sources.Count;

    /// <summary>按名称在所有就绪源中查找，首个命中返回。</summary>
    public ControlLookupResult FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ControlLookupResult.Miss;

        foreach (var source in GetOrderedSnapshot())
        {
            if (!source.IsReady)
                continue;

            var control = source.FindByName(name);
            if (control != null)
                return new ControlLookupResult(control, source);
        }

        return ControlLookupResult.Miss;
    }

    /// <summary>在指定根控件下按名称查找，首个命中返回。</summary>
    public ControlLookupResult FindByName(Control root, string name)
    {
        if (root == null || string.IsNullOrWhiteSpace(name))
            return ControlLookupResult.Miss;

        foreach (var source in GetOrderedSnapshot())
        {
            if (!source.IsReady)
                continue;

            var control = source.FindByName(root, name);
            if (control != null)
                return new ControlLookupResult(control, source);
        }

        return ControlLookupResult.Miss;
    }

    private IControlLookupSource[] GetOrderedSnapshot()
    {
        if (_dirty)
        {
            _sources.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _dirty = false;
        }

        return _sources.ToArray();
    }
}

/// <summary>
/// 便捷门面：以 <see cref="ControlLookupRegistry"/> 为默认查找内核的单例入口，
/// 供未走 DI 的场景（静态工具、审计）复用同一内核。
/// </summary>
public static class ControlLookup
{
    public static ControlLookupRegistry Default { get; } = new();
}
