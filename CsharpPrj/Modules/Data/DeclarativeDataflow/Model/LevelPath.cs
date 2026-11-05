using System.Collections.Generic;
using System.Linq;

namespace Modules.DeclarativeDataflow.Model;

/// <summary>
/// 层次路径（根 → 当前，含两端）：同类型实体可出现在不同深度，故寻址必须携带路径而非单点。
/// <para>
/// 不可变（<see cref="Append"/> 返回新实例），供递归引擎在每层构造"当前上下文路径"：
/// <see cref="Depth"/> 供深度护栏判定，<see cref="Contains"/> 供环守卫判定（路径上已出现 = 真环）。
/// </para>
/// </summary>
public sealed class LevelPath
{
    private readonly List<LevelRef> _segments;

    private LevelPath(List<LevelRef> segments) => _segments = segments;

    /// <summary>以根节点起一条路径。</summary>
    public static LevelPath Root(LevelRef root)
    {
        root.EnsureValid();
        return new LevelPath(new List<LevelRef>(1) { root });
    }

    /// <summary>根 → 当前的段序列（含两端）。</summary>
    public IReadOnlyList<LevelRef> Segments => _segments;

    /// <summary>当前深度（段数；根 = 1）。</summary>
    public int Depth => _segments.Count;

    /// <summary>追加下一层节点，返回新路径（不可变语义）。</summary>
    public LevelPath Append(LevelRef child)
    {
        child.EnsureValid();
        var next = new List<LevelRef>(_segments.Count + 1);
        next.AddRange(_segments);
        next.Add(child);
        return new LevelPath(next);
    }

    /// <summary>路径上是否已出现该节点（环守卫：自环支持不定深度，真环必须被路径捕获）。按实体值相等判定。</summary>
    public bool Contains(LevelRef node) => _segments.Contains(node);

    public override string ToString() => string.Join(" > ", _segments.Select(s => s.Key));
}
