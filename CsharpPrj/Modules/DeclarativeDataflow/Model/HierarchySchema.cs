using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.DeclarativeDataflow.Model;

/// <summary>
/// 层次 Schema —— "层数不定"的唯一外置点。
/// <para>
/// 由 <see cref="RootLevelId"/> + 层声明集合构成：层数 N 由数据决定（异构 N 层），
/// 某层的 <see cref="HierarchyLevelSpec.Contains"/> 含自身即表达**不定深度**（同构递归）。
/// 框架不解释 <see cref="HierarchyLevelSpec.EntityType"/> 的存储语义，只按层组织遍历与处理。
/// </para>
/// <para>
/// 构造期只做"最小结构断言"（非空层集 / 层 Id 唯一 / 根已声明）；
/// 完整校验（包含边两端已声明、根可达、层间环）由 <see cref="HierarchySchemaValidator"/> 给出诊断。
/// </para>
/// </summary>
public sealed class HierarchySchema
{
    private readonly Dictionary<string, HierarchyLevelSpec> _byId;
    private readonly List<HierarchyLevelSpec> _ordered;

    /// <param name="rootLevelId">根层 Id（必须出现在 <paramref name="levels"/> 中）。</param>
    /// <param name="levels">层声明集合（保序；层 Id 唯一）。</param>
    public HierarchySchema(string rootLevelId, IEnumerable<HierarchyLevelSpec> levels)
    {
        if (string.IsNullOrWhiteSpace(rootLevelId))
            throw new ArgumentException("层次 schema 的根层 Id 不可为空。", nameof(rootLevelId));
        ArgumentNullException.ThrowIfNull(levels);

        _ordered = levels.ToList();
        if (_ordered.Count == 0)
            throw new ArgumentException("层次 schema 至少需要一层声明。", nameof(levels));

        _byId = new Dictionary<string, HierarchyLevelSpec>(StringComparer.Ordinal);
        foreach (var level in _ordered)
        {
            if (level is null)
                throw new ArgumentException("层次 schema 含空层声明。", nameof(levels));
            if (string.IsNullOrWhiteSpace(level.LevelId))
                throw new ArgumentException("层声明 LevelId 不可为空。", nameof(levels));
            if (!_byId.TryAdd(level.LevelId, level))
                throw new ArgumentException($"层 Id 重复：'{level.LevelId}'。", nameof(levels));
        }

        if (!_byId.ContainsKey(rootLevelId))
            throw new ArgumentException($"根层 '{rootLevelId}' 未在层声明中出现。", nameof(rootLevelId));

        RootLevelId = rootLevelId;
        Levels = _ordered.AsReadOnly();
    }

    /// <summary>根层 Id（遍历起点）。</summary>
    public string RootLevelId { get; }

    /// <summary>全部层声明（声明序）。</summary>
    public IReadOnlyList<HierarchyLevelSpec> Levels { get; }

    /// <summary>根层声明。</summary>
    public HierarchyLevelSpec Root => _byId[RootLevelId];

    /// <summary>是否存在自环层（不定深度可能出现 → 必须配深度预算）。</summary>
    public bool HasSelfRecursion => _ordered.Any(l => l.ContainsSelf);

    /// <summary>按 Id 取层声明；未声明返回 null。</summary>
    public HierarchyLevelSpec? Level(string levelId)
        => !string.IsNullOrWhiteSpace(levelId) && _byId.TryGetValue(levelId, out var spec) ? spec : null;

    /// <summary>该层的出向包含声明集；层未声明返回空集（统一的空值兜底入口）。</summary>
    public IReadOnlyList<HierarchyContainment> Containments(string parentLevelId)
        => Level(parentLevelId)?.Contains ?? Array.Empty<HierarchyContainment>();

    /// <summary>该层的子层 Id 集（保序、去重）；层未声明返回空集。层次遍历的唯一子层枚举入口。</summary>
    public IReadOnlyList<string> ChildLevelIds(string levelId)
        => Level(levelId)?.ChildLevelIds ?? Array.Empty<string>();

    /// <summary>取指定父子层之间的包含声明；不存在返回 null。</summary>
    public HierarchyContainment? Containment(string parentLevelId, string childLevelId)
        => Containments(parentLevelId)
            .FirstOrDefault(c => string.Equals(c.ChildLevelId, childLevelId, StringComparison.Ordinal));

    /// <summary>父层是否可直接包含子层。</summary>
    public bool CanContain(string parentLevelId, string childLevelId)
        => Containment(parentLevelId, childLevelId) is not null;

    /// <summary>全部"父层 → 子层"包含对（展开自各层声明，供拓扑与校验消费）。</summary>
    public IEnumerable<(HierarchyLevelSpec Parent, HierarchyContainment Containment)> ContainmentPairs()
    {
        foreach (var level in _ordered)
        foreach (var containment in Containments(level.LevelId))
            yield return (level, containment);
    }

    /// <summary>
    /// 自根按包含边广度优先展开的层序（自环不推进可达性、未声明层跳过；**不含**不可达层兜底）。
    /// <para>
    /// 可达性判定的唯一实现：校验器（可达性诊断）与 SchemaTopology.LevelOrder 共用，
    /// 避免同一图遍历在多处漂移。非法 schema 的兜底/报错职责分别由调用方与
    /// <see cref="HierarchySchemaValidator"/> 承担。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> LevelOrderFromRoot()
    {
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { RootLevelId };
        var queue = new Queue<string>();
        queue.Enqueue(RootLevelId);
        while (queue.Count > 0)
        {
            var levelId = queue.Dequeue();
            order.Add(levelId);
            foreach (var childId in ChildLevelIds(levelId))
            {
                // 自环不推进可达性（避免自我循环掩盖不可达层）；未声明层由校验器另报。
                if (string.Equals(childId, levelId, StringComparison.Ordinal)) continue;
                if (Level(childId) is null) continue;
                if (seen.Add(childId)) queue.Enqueue(childId);
            }
        }
        return order;
    }

    public override string ToString()
        => $"HierarchySchema(root={RootLevelId}, levels=[{string.Join(", ", _ordered.Select(l => l.LevelId))}])";
}
