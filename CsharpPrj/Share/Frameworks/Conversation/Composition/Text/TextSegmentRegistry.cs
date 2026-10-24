#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Composition.Text;

/// <summary>段声明提供者 —— 领域侧把自身段集合登记到通用注册表的唯一入口。</summary>
public interface ISegmentProvider
{
    /// <summary>声明本提供者负责的段（键全局唯一）。</summary>
    IEnumerable<TextSpec> Declare();
}

/// <summary>
/// 段注册表 —— 声明的集中登记处（键唯一、组非空为启动期硬约束）。
/// <para>
/// 注册表只持有声明，不持有取值与顺序策略；计划由 <see cref="Plan"/> 从同一批声明派生，
/// 契约由 <see cref="TextPlan.Contract"/> 再派生，因此「声明 → 计划 → 契约」永远同一份事实。
/// </para>
/// <para>重复键/空键/无组 ⇒ 构造期抛（配置错误必须启动期红，不得静默取其一）。</para>
/// </summary>
public sealed class TextSegmentRegistry
{
    private readonly List<TextSpec> _specs;
    private readonly Dictionary<string, TextSpec> _byKey;

    /// <summary>由声明集构造（重复键 ⇒ 抛）。</summary>
    public TextSegmentRegistry(IEnumerable<TextSpec>? specs)
    {
        _specs = new List<TextSpec>();
        _byKey = new Dictionary<string, TextSpec>(StringComparer.Ordinal);

        foreach (var spec in specs ?? Enumerable.Empty<TextSpec>())
        {
            ArgumentNullException.ThrowIfNull(spec);
            if (spec.Key.IsEmpty)
                throw new InvalidOperationException("段键不得为空（声明错误）");
            if (string.IsNullOrWhiteSpace(spec.Group))
                throw new InvalidOperationException($"段 {spec.Key.Value} 缺组标签（声明错误）");
            if (!_byKey.TryAdd(spec.Key.Value, spec))
                throw new InvalidOperationException($"段键重复注册：{spec.Key.Value}（声明面必须唯一）");
            _specs.Add(spec);
        }
    }

    /// <summary>空注册表。</summary>
    public static TextSegmentRegistry Empty { get; } = new(Array.Empty<TextSpec>());

    /// <summary>全部声明（登记序）。</summary>
    public IReadOnlyList<TextSpec> Specs => _specs;

    /// <summary>由多个提供者合并登记（重复键 ⇒ 抛）。</summary>
    public static TextSegmentRegistry From(IEnumerable<ISegmentProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        return new TextSegmentRegistry(providers.SelectMany(static p => p.Declare()));
    }

    /// <summary>追加一个提供者（返回新注册表，不可变语义）。</summary>
    public TextSegmentRegistry With(ISegmentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new TextSegmentRegistry(_specs.Concat(provider.Declare()));
    }

    /// <summary>按键取声明（不存在 → null）。</summary>
    public TextSpec? SpecOf(string key)
        => !string.IsNullOrEmpty(key) && _byKey.TryGetValue(key, out var spec) ? spec : null;

    /// <summary>由本注册表生成计划。</summary>
    public TextPlan Plan(ISegmentContext context, string profileKey = "")
        => TextPlanBuilder.Build(_specs, context, profileKey);
}
