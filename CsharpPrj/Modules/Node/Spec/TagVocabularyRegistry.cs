namespace Modules.Node.Spec;

/// <summary>轴化词表注册表实现（无进程静态状态；装配期登记、运行期只读）。</summary>
public sealed class TagVocabularyRegistry : ITagVocabularyRegistry
{
    private readonly Dictionary<string, ITagVocabulary> _axes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _diagnostics = new();
    private bool _frozen;

    /// <inheritdoc />
    public bool IsFrozen => _frozen;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Axes => _axes.Keys.ToArray();

    /// <inheritdoc />
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    /// <inheritdoc />
    public void Register(ITagVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentException.ThrowIfNullOrWhiteSpace(vocabulary.Axis);
        if (_frozen)
        {
            throw new InvalidOperationException("词表注册表已冻结：运行期禁止登记");
        }

        if (_axes.TryGetValue(vocabulary.Axis, out var existing))
        {
            if (SameMapping(existing, vocabulary))
            {
                return;
            }

            _diagnostics.Add($"axis.conflict|{vocabulary.Axis}|同轴异映射：拒绝覆盖（不静默降级）");
            return;
        }

        _axes[vocabulary.Axis] = vocabulary;
    }

    /// <inheritdoc />
    public void Freeze() => _frozen = true;

    /// <inheritdoc />
    public bool TryGet(string axis, out ITagVocabulary vocabulary)
        => _axes.TryGetValue(axis ?? string.Empty, out vocabulary!);

    /// <inheritdoc />
    public bool TryResolve(string axis, string token, out int bit)
    {
        bit = 0;
        if (string.IsNullOrWhiteSpace(token) || !_axes.TryGetValue(axis ?? string.Empty, out var vocabulary))
        {
            return false;
        }

        return vocabulary.Marks.TryGetValue(token, out bit);
    }

    private static bool SameMapping(ITagVocabulary left, ITagVocabulary right)
    {
        if (left.Marks.Count != right.Marks.Count)
        {
            return false;
        }

        foreach (var pair in left.Marks)
        {
            if (!right.Marks.TryGetValue(pair.Key, out var value) || value != pair.Value)
            {
                return false;
            }
        }

        return true;
    }
}
