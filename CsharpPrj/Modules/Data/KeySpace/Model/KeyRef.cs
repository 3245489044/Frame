namespace Modules.KeySpace.Model;

/// <summary>
/// 键的结构路径（段序即语义）。结构相等（段内容 + 段序），不依赖底层集合实现。
/// </summary>
public sealed class KeyPath : IEquatable<KeyPath>
{
    /// <summary>空路径（无结构段）。</summary>
    public static readonly KeyPath Empty = new(Array.Empty<string>());

    private readonly string[] _segments;

    public KeyPath(IEnumerable<string>? segments)
        => _segments = (segments ?? Array.Empty<string>())
            .Where(static segment => !string.IsNullOrEmpty(segment))
            .ToArray();

    /// <summary>结构段（只读；序 = 声明序）。</summary>
    public IReadOnlyList<string> Segments => _segments;

    public bool IsEmpty => _segments.Length == 0;

    public bool Equals(KeyPath? other)
        => other is not null && _segments.SequenceEqual(other._segments, StringComparer.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as KeyPath);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in _segments)
        {
            hash.Add(segment, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => string.Join("/", _segments);
}

/// <summary>
/// 键的语义引用：族 + 原值 + 结构路径。字符串只能经本类型进入框架。
/// </summary>
public readonly record struct KeyRef(string Family, string Value, KeyPath Path)
{
    /// <summary>
    /// 域标识（K-01 修复点）：默认 = 未声明域，保持既有构造点零改动；
    /// 域绑定调用点经具名工厂/<see cref="Of(string,string,KeyDomainId)"/> 升级。
    /// </summary>
    public KeyDomainId Domain { get; init; }

    /// <summary>空引用（族/值/路径全空）。</summary>
    public bool IsEmpty
        => string.IsNullOrEmpty(Family) && string.IsNullOrEmpty(Value) && (Path is null || Path.IsEmpty);

    public static KeyRef Of(string family, string value)
        => new(family, value, KeyPath.Empty);

    /// <summary>带域构造（跨域边界唯一推荐入口）。</summary>
    public static KeyRef Of(string family, string value, KeyDomainId domain)
        => new(family, value, KeyPath.Empty) { Domain = domain };

    /// <summary>补域（迁移期：既有无域引用补上声明域，不改变族/值/路径）。</summary>
    public KeyRef WithDomain(KeyDomainId domain) => this with { Domain = domain };

    /// <summary>带域文本（持久化/诊断口径，R-K5）：已声明域带 <c>domain:</c> 前缀，未声明域保持原文本。</summary>
    public string ToScopedString() => Domain.IsDeclared ? $"{Domain.Id}:{Family}:{Value}" : ToString();

    public override string ToString() => IsEmpty ? "(empty)" : $"{Family}:{Value}";
}
