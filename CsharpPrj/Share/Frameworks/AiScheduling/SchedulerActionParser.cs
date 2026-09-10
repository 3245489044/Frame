using System;
using System.Linq;

namespace Share.AiScheduling;

/// <summary>
/// 调度动作名宽容解析（单一实现）：兼容 snake_case / kebab-case / camelCase / enum 名。
/// 供各导演（LLM / 配置）共用，消除重复实现。
/// </summary>
public static class SchedulerActionParser
{
    // 预归一化候选（枚举名不含 _ / -，归一化仅一次；避免每次调用在循环内重复 Replace）。
    private static readonly (SchedulerAction Action, string Normalized)[] Candidates =
        Enum.GetValues<SchedulerAction>()
            .Select(v => (v, Enum.GetName(v)!.Replace("_", string.Empty).Replace("-", string.Empty)))
            .ToArray();

    public static bool TryParse(string? s, out SchedulerAction action)
    {
        action = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        var norm = s.Replace("_", string.Empty).Replace("-", string.Empty);
        foreach (var (candidate, normalized) in Candidates)
        {
            if (string.Equals(norm, normalized, StringComparison.OrdinalIgnoreCase))
            {
                action = candidate;
                return true;
            }
        }
        return false;
    }
}
