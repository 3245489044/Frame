using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.Parsing.Invocation;

/// <summary>命名空间语义（单一来源：空间的「何时可判定」属性在此定义）。</summary>
public static class NameSpaceIds
{
    /// <summary>
    /// 运行期产出空间：名字由渲染/前序命令产生（控件、容器宿主、作用域）。
    /// 加载期不可判定 ⇒ 不据其缺席判违约（须由执行期断言按真实寻址给出类别）。
    /// </summary>
    public static bool IsRuntimeProduced(NameSpaceId space)
        => space is NameSpaceId.Control or NameSpaceId.ContainerHost or NameSpaceId.Scope;
}

/// <summary>
/// 调用面命名空间（唯一枚举）：目标名必须落在且只落在其中一个空间里。
/// 消灭「所有 name 压平成单一集合」造成的假绿（views[].name 被误当容器宿主）。
/// </summary>
public enum NameSpaceId
{
    None = 0,
    Control,
    ContainerHost,
    Scope,
    ProbeFunction,
    ConfigFile,
    Form,
}

/// <summary>命名空间事实出处：Declared=声明面清单（加载期、无 UI 可复算），Live=运行期探针（覆盖声明）。</summary>
public enum NameSpaceOrigin
{
    Declared = 0,
    Live = 1,
}

/// <summary>
/// 命名空间来源端口（唯一扩展点）：宿主只提供「这个空间里有哪些名字 / 有没有这个名字」，
/// 索引、优先级、来源可见化全部由 <see cref="NameSpaceIndex"/> 承担。
/// </summary>
public interface INameSpaceSource
{
    NameSpaceId Space { get; }

    NameSpaceOrigin Origin { get; }

    IEnumerable<string> Names { get; }

    bool Contains(string name);
}

/// <summary>解析命中三态（Unavailable = 该空间未注册来源，不是「不存在」）。</summary>
public enum NameHitKind
{
    Found = 0,
    NotFound = 1,
    Unavailable = 2,
}

/// <summary>一次寻址结果（结构事实：空间 / 名字 / 来源）。</summary>
public readonly record struct NameHit(NameHitKind Kind, NameSpaceId Space, string? Name, NameSpaceOrigin? Origin)
{
    public bool Found => Kind == NameHitKind.Found;

    public bool IsUnavailable => Kind == NameHitKind.Unavailable;

    public static NameHit Of(NameSpaceId space, string? name, NameSpaceOrigin origin)
        => new(NameHitKind.Found, space, name, origin);

    public static NameHit Miss(NameSpaceId space, string? name, NameSpaceOrigin origin)
        => new(NameHitKind.NotFound, space, name, origin);

    public static NameHit NoSource(NameSpaceId space, string? name)
        => new(NameHitKind.Unavailable, space, name, null);

    public string Describe()
        => Kind switch
        {
            NameHitKind.Found => $"在 {Space} 空间命中（来源 {Origin}）",
            NameHitKind.NotFound => $"不在 {Space} 空间（来源 {Origin}）",
            _ => $"{Space} 空间无来源（该校验维度未参与）",
        };
}

/// <summary>委托型来源（宿主装配期注册；Names 可缺省 = 只支持谓词判定）。</summary>
public sealed class DelegateNameSpaceSource : INameSpaceSource
{
    private readonly Func<string, bool> _contains;
    private readonly Func<IEnumerable<string>>? _names;

    public DelegateNameSpaceSource(
        NameSpaceId space,
        Func<string, bool> contains,
        NameSpaceOrigin origin = NameSpaceOrigin.Live,
        Func<IEnumerable<string>>? names = null)
    {
        if (space == NameSpaceId.None) throw new ArgumentOutOfRangeException(nameof(space), "None 不是可寻址空间。");
        _contains = contains ?? throw new ArgumentNullException(nameof(contains));
        _names = names;
        Space = space;
        Origin = origin;
    }

    public NameSpaceId Space { get; }

    public NameSpaceOrigin Origin { get; }

    public IEnumerable<string> Names => _names?.Invoke() ?? Array.Empty<string>();

    public bool Contains(string name) => !string.IsNullOrWhiteSpace(name) && _contains(name);
}

/// <summary>集合型来源（声明面清单 / 已注册名集合）。</summary>
public sealed class SetNameSpaceSource : INameSpaceSource
{
    private readonly HashSet<string> _names;

    public SetNameSpaceSource(NameSpaceId space, IEnumerable<string> names, NameSpaceOrigin origin)
    {
        if (space == NameSpaceId.None) throw new ArgumentOutOfRangeException(nameof(space), "None 不是可寻址空间。");
        ArgumentNullException.ThrowIfNull(names);
        Space = space;
        Origin = origin;
        _names = new HashSet<string>(names.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
    }

    public NameSpaceId Space { get; }

    public NameSpaceOrigin Origin { get; }

    public IEnumerable<string> Names => _names;

    public bool Contains(string name) => !string.IsNullOrWhiteSpace(name) && _names.Contains(name);
}
