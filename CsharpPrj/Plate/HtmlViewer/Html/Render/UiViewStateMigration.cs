#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>状态迁移结果：迁移后的交互态 + 被丢弃项（结构事实，供诊断/门禁复算）。</summary>
public sealed record UiViewStateMigrationResult(
    UiInteractionSnapshot? Migrated,
    IReadOnlyList<string> Dropped)
{
    /// <summary>无交互态/无迁移声明。</summary>
    public static UiViewStateMigrationResult None { get; } =
        new(null, Array.Empty<string>());
}

/// <summary>
/// 视图状态迁移（VSC-4）：按视图程序 <c>stateMigration[]</c> 声明选择性回放（输入/滚动/焦点/折叠）。
/// <para>纪律：迁移是**声明**，不是渲染层临时拷贝——未声明项一律丢弃并产 <c>view_state_dropped</c> 诊断。</para>
/// <para>判据载体 = 声明表与快照键集合（结构事实），不含执行者叙述。</para>
/// </summary>
public static class UiViewStateMigrationPolicy
{
    /// <summary>迁移种类受控词（声明面 <c>stateMigration[].kind</c>）。</summary>
    public const string KindInput = "input";
    public const string KindScroll = "scroll";
    public const string KindFocus = "focus";

    /// <summary>
    /// 按声明裁剪交互快照：只保留声明的 (kind,key)；未声明项进 <see cref="UiViewStateMigrationResult.Dropped"/>。
    /// </summary>
    /// <param name="blueprint">目标视图程序（null ⇒ 不迁移、不丢弃，原样返回）。</param>
    /// <param name="interaction">跨视图携带的交互态（上一视图采集）。</param>
    public static UiViewStateMigrationResult Apply(UiViewBlueprint? blueprint, UiInteractionSnapshot? interaction)
    {
        if (blueprint is null || interaction is null || interaction.IsEmpty) return UiViewStateMigrationResult.None;

        var declared = blueprint.StateMigrations
            .GroupBy(static m => m.Kind, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.Select(m => m.Key).ToHashSet(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);

        var dropped = new List<string>();

        IReadOnlyDictionary<string, string?>? inputs = null;
        if (interaction.Inputs is { Count: > 0 } sourceInputs)
        {
            var kept = new Dictionary<string, string?>(StringComparer.Ordinal);
            var allowed = declared.TryGetValue(KindInput, out var inputKeys) ? inputKeys : null;
            foreach (var pair in sourceInputs)
            {
                if (allowed is not null && allowed.Contains(pair.Key))
                {
                    kept[pair.Key] = pair.Value;
                }
                else
                {
                    dropped.Add(KindInput + ":" + pair.Key);
                }
            }

            inputs = kept;
        }

        IReadOnlyList<UiScrollState>? scroll = null;
        if (interaction.Scroll is { Count: > 0 } sourceScroll)
        {
            var kept = new List<UiScrollState>(sourceScroll.Count);
            var allowed = declared.TryGetValue(KindScroll, out var scrollKeys) ? scrollKeys : null;
            // 滚动键 = 容器键（精确匹配）；未声明 ⇒ 丢弃（切视图后锚点不存在属结构性丢弃）。
            foreach (var state in sourceScroll)
            {
                if (allowed is not null && allowed.Contains(state.Key))
                {
                    kept.Add(state);
                }
                else
                {
                    dropped.Add(KindScroll + ":" + state.Key);
                }
            }

            scroll = kept;
        }

        string? focus = null;
        if (!string.IsNullOrWhiteSpace(interaction.Focus))
        {
            var allowed = declared.TryGetValue(KindFocus, out var focusKeys) ? focusKeys : null;
            // 焦点判据 = 元素键精确等值（禁子串）。未声明/声明为空 ⇒ 丢弃（与 input/scroll 同口径）。
            if (allowed is { Count: > 0 } && allowed.Contains(interaction.Focus!))
            {
                focus = interaction.Focus;
            }
            else
            {
                dropped.Add(KindFocus + ":" + interaction.Focus);
            }
        }

        var migrated = new UiInteractionSnapshot(focus, scroll, inputs);
        return new UiViewStateMigrationResult(migrated, dropped);
    }
}
