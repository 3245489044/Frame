using System;
using System.Collections.Generic;

namespace Operation.HierarchyMethod.Nodes;

/// <summary>
/// 轮次种子键名（框架侧字面单源；与 Agent 侧 <c>RoundKeys</c> 同字面，框架不持有领域词汇）。
/// </summary>
public static class SeedInputKeys
{
    /// <summary>本轮输入权威键（租约派生只读此键）。</summary>
    public const string Input = "input";

    /// <summary>兼容读别名（旧键；只读不写，迁移期兼容）。</summary>
    public const string LegacyUserInput = "user_input";
}

/// <summary>
/// 轮次启动输入解析**唯一实现**（SingleImplementation）：按 <c>input → UserInput → 兼容键 user_input</c>
/// 的固定优先级取种子；三者皆空 ⇒ 显式失败（禁空串进入骨架）。
/// <para>消费方：执行租约工厂（每轮租约播种，Agent 侧）与 Agent 侧 <c>LeaseDeriver</c>（同字面守卫收敛到本实现）。</para>
/// </summary>
public static class SeedInput
{
    /// <summary>按固定优先级解析；返回 false = 无可用输入（调用方决定显式失败或可见化）。</summary>
    public static bool TryResolve(IDictionary<string, object>? data, string? userInput, out string value)
    {
        value = string.Empty;

        if (TryRead(data, SeedInputKeys.Input, out var input))
        {
            value = input;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(userInput))
        {
            value = userInput!;
            return true;
        }

        if (TryRead(data, SeedInputKeys.LegacyUserInput, out var legacy))
        {
            value = legacy;
            return true;
        }

        return false;
    }

    /// <summary>解析或显式失败（错误文案单源：seed.user-input-missing: input）。</summary>
    public static string Require(IDictionary<string, object>? data, string? userInput)
        => TryResolve(data, userInput, out var value)
            ? value
            : throw new InvalidOperationException("seed.user-input-missing: input");

    private static bool TryRead(IDictionary<string, object>? data, string key, out string value)
    {
        value = string.Empty;
        if (data is null || !data.TryGetValue(key, out var raw) || raw is not string text || string.IsNullOrWhiteSpace(text))
            return false;
        value = text;
        return true;
    }
}
