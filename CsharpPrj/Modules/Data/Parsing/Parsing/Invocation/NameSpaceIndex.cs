using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Plate.JsonMethod.Serialization;

namespace Modules.Parsing.Invocation;

/// <summary>
/// 声明面名字清单（数据）：按空间分组的已声明名，供无 UI 环境复算。
/// 只承载「空间 → 名字集」，不承载任何控件/容器实体。
/// </summary>
public sealed class NameSpaceManifest
{
    private readonly Dictionary<NameSpaceId, string[]> _spaces;

    public NameSpaceManifest(IReadOnlyDictionary<NameSpaceId, IEnumerable<string>> spaces)
    {
        ArgumentNullException.ThrowIfNull(spaces);
        _spaces = spaces.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static NameSpaceManifest Empty { get; } = new(new Dictionary<NameSpaceId, IEnumerable<string>>());

    public IReadOnlyCollection<NameSpaceId> Spaces => _spaces.Keys;

    public IReadOnlyList<string> Names(NameSpaceId space)
        => _spaces.TryGetValue(space, out var names) ? names : Array.Empty<string>();

    public INameSpaceSource AsSource(NameSpaceId space)
        => new SetNameSpaceSource(space, Names(space), NameSpaceOrigin.Declared);

    /// <summary>按清单注册全部空间来源（Declared 级；Live 探针后续注册会覆盖）。</summary>
    public void RegisterAll(NameSpaceIndex? index = null)
    {
        foreach (var space in _spaces.Keys)
            (index ?? NameSpaceIndex.Default).Register(AsSource(space));
    }

    /// <summary>读清单 JSON（复用 Plate 反序列化，不新增 JSON 逻辑）。</summary>
    public static NameSpaceManifest Load(string json)
    {
        var document = JsonSerializationService.Instance.Deserialize<NameSpaceManifestDocument>(json, JsonContext.UIConfig);
        if (document?.Spaces is null) return Empty;

        var spaces = new Dictionary<NameSpaceId, IEnumerable<string>>();
        foreach (var (key, values) in document.Spaces)
        {
            if (!Enum.TryParse<NameSpaceId>(key, ignoreCase: true, out var space) || !Enum.IsDefined(space))
                continue;
            spaces[space] = values ?? Array.Empty<string>();
        }

        return new NameSpaceManifest(spaces);
    }
}

/// <summary>清单 JSON 的 POCO（字段名对齐声明面）。</summary>
public sealed class NameSpaceManifestDocument
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    [JsonPropertyName("spaces")]
    public Dictionary<string, string[]>? Spaces { get; set; }
}

/// <summary>
/// 命名空间索引（唯一寻址入口）：合并多来源，Live 覆盖 Declared，命中带来源可见化。
/// 未注册来源 ⇒ Unavailable（而不是伪造 NotFound，避免把「没校验」当成「不存在」）。
/// </summary>
public sealed class NameSpaceIndex
{
    /// <summary>进程级默认索引（宿主装配期注册；测试可自建实例）。</summary>
    public static NameSpaceIndex Default { get; } = new();

    private readonly object _gate = new();
    private readonly Dictionary<NameSpaceId, Dictionary<NameSpaceOrigin, INameSpaceSource>> _sources = new();

    public void Register(INameSpaceSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            if (!_sources.TryGetValue(source.Space, out var byOrigin))
            {
                byOrigin = new Dictionary<NameSpaceOrigin, INameSpaceSource>();
                _sources[source.Space] = byOrigin;
            }

            byOrigin[source.Origin] = source;
        }
    }

    public IReadOnlyCollection<NameSpaceId> RegisteredSpaces
    {
        get { lock (_gate) { return _sources.Keys.ToArray(); } }
    }

    public NameHit Resolve(NameSpaceId space, string? name)
    {
        if (space == NameSpaceId.None || string.IsNullOrWhiteSpace(name))
            return NameHit.NoSource(space, name);

        var source = Pick(space);
        if (source is null) return NameHit.NoSource(space, name);
        return source.Contains(name)
            ? NameHit.Of(space, name, source.Origin)
            : NameHit.Miss(space, name, source.Origin);
    }

    public IReadOnlyList<string> Names(NameSpaceId space)
        => Pick(space)?.Names.ToArray() ?? Array.Empty<string>();

    public void Reset()
    {
        lock (_gate) { _sources.Clear(); }
    }

    private INameSpaceSource? Pick(NameSpaceId space)
    {
        lock (_gate)
        {
            if (!_sources.TryGetValue(space, out var byOrigin)) return null;
            if (byOrigin.TryGetValue(NameSpaceOrigin.Live, out var live)) return live;
            return byOrigin.TryGetValue(NameSpaceOrigin.Declared, out var declared) ? declared : null;
        }
    }
}

/// <summary>静态门面（宿主装配期只认这一处；内部委托进程级默认索引）。</summary>
public static class NameSpaceRegistry
{
    public static void Register(INameSpaceSource source) => NameSpaceIndex.Default.Register(source);

    public static void RegisterManifest(NameSpaceManifest manifest) => manifest?.RegisterAll();

    public static NameHit Resolve(NameSpaceId space, string? name) => NameSpaceIndex.Default.Resolve(space, name);

    public static IReadOnlyList<string> Names(NameSpaceId space) => NameSpaceIndex.Default.Names(space);

    public static IReadOnlyCollection<NameSpaceId> RegisteredSpaces => NameSpaceIndex.Default.RegisteredSpaces;
}
