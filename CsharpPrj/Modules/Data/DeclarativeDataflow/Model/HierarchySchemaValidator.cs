using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.DeclarativeDataflow.Model;

/// <summary>
/// 层次 Schema 校验器（装载期 fail-fast，纯函数、零 IO）。
/// <para>
/// 构造期 <see cref="HierarchySchema"/> 只做最小结构断言；本校验器给出完整诊断：
/// ① 包含边目标层已声明；② 全部层自根可达；③ 非自环的层间环。
/// </para>
/// <para>
/// 自环本身合法（不定深度表达），不作为问题；"自环层须配深度预算"的收口不在本校验器
/// （装载期无法判定运行期预算），由 <see cref="Processing.HierarchyProcessPlan.EffectiveBudget"/>
/// 与 <see cref="Budget.ProcessBudget"/> 在运行期强制。
/// </para>
/// </summary>
public static class HierarchySchemaValidator
{
    /// <summary>校验并返回全部问题（空集 = 合法）。自环本身合法，不作为问题。</summary>
    public static IReadOnlyList<string> Validate(HierarchySchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var issues = new List<string>();

        // ① 包含边目标层必须已声明（含自环：自环目标即本层，必然已声明）。
        foreach (var (parent, containment) in schema.ContainmentPairs())
        {
            if (string.IsNullOrWhiteSpace(containment.ChildLevelId))
            {
                issues.Add($"层 '{parent.LevelId}' 含空的包含声明（ChildLevelId 缺失）。");
                continue;
            }
            if (schema.Level(containment.ChildLevelId) is null)
                issues.Add($"层 '{parent.LevelId}' 的包含声明指向未声明的子层 '{containment.ChildLevelId}'。");
            if (string.IsNullOrWhiteSpace(containment.ContainsEdgeType))
                issues.Add($"层 '{parent.LevelId}' → '{containment.ChildLevelId}' 的包含边类型为空。");
        }

        // ② 可达性：与拓扑共用同一自根 BFS（自环不推进，未声明层已由 ① 报出）。
        var reachable = new HashSet<string>(schema.LevelOrderFromRoot(), StringComparer.Ordinal);
        foreach (var level in schema.Levels)
            if (!reachable.Contains(level.LevelId))
                issues.Add($"层 '{level.LevelId}' 自根 '{schema.RootLevelId}' 不可达（包含边断裂）。");

        // ③ 层间环（排除自环；自环是合法的"不定深度"表达，层间环则使拓扑无起点）。
        var cycle = FindLevelCycle(schema);
        if (cycle is not null)
            issues.Add($"层间存在环（非自环）：{string.Join(" → ", cycle)}。");

        return issues;
    }

    /// <summary>校验失败即抛 <see cref="InvalidOperationException"/>（列明全部问题）。</summary>
    public static void ThrowIfInvalid(HierarchySchema schema)
    {
        var issues = Validate(schema);
        if (issues.Count == 0) return;
        throw new InvalidOperationException(
            "层次 Schema 非法：" + Environment.NewLine + " - " + string.Join(Environment.NewLine + " - ", issues));
    }

    /// <summary>DFS 着色找层间环（排除自环）；返回环路径（首尾同层）或 null。</summary>
    private static IReadOnlyList<string>? FindLevelCycle(HierarchySchema schema)
    {
        const int white = 0, gray = 1, black = 2;
        var color = schema.Levels.ToDictionary(l => l.LevelId, _ => white, StringComparer.Ordinal);
        var stack = new List<string>();

        foreach (var level in schema.Levels)
        {
            var cycle = Visit(level.LevelId);
            if (cycle is not null) return cycle;
        }
        return null;

        IReadOnlyList<string>? Visit(string levelId)
        {
            color[levelId] = gray;
            stack.Add(levelId);
            foreach (var childId in schema.ChildLevelIds(levelId))
            {
                if (string.Equals(childId, levelId, StringComparison.Ordinal)) continue; // 自环合法
                if (!color.ContainsKey(childId)) continue;                              // 未声明层：① 已报
                if (color[childId] == gray)
                {
                    var start = stack.IndexOf(childId);
                    var cyclePath = stack.Skip(start).ToList();
                    cyclePath.Add(childId);
                    return cyclePath;
                }
                if (color[childId] == white)
                {
                    var found = Visit(childId);
                    if (found is not null) return found;
                }
            }
            stack.RemoveAt(stack.Count - 1);
            color[levelId] = black;
            return null;
        }
    }
}
