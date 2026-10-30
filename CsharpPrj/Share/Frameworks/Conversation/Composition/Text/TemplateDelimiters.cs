#nullable enable

using System;

namespace Share.Composition.Text;

/// <summary>
/// 模板定界符对 —— 同一渲染实例只使用一对，禁止自动混用。
/// <para>
/// 不混用是必要约束：<c>{x}</c> 与 <c>{{x}}</c> 的前缀互相包含，若同一趟同时识别，
/// 会先匹配 <c>{</c> 再把 <c>{{x}}</c> 的中间 token 当成槽，产生二次替换。调用方按用途显式选择。
/// </para>
/// </summary>
/// <param name="Prefix">前缀（如 <c>{</c> / <c>&lt;&lt;</c> / <c>{{</c>）。</param>
/// <param name="Suffix">后缀（如 <c>}</c> / <c>&gt;&gt;</c> / <c>}}</c>）。</param>
public sealed record TemplateDelimiters(string Prefix, string Suffix)
{
    /// <summary>单大括号：<c>{slot}</c>（Agent 标量槽默认）。</summary>
    public static TemplateDelimiters Brace { get; } = new("{", "}");

    /// <summary>双尖括号：<c>&lt;&lt;slot&gt;&gt;</c>（Agent 节点槽，第二阶段）。</summary>
    public static TemplateDelimiters Angle { get; } = new("<<", ">>");

    /// <summary>双大括号：<c>{{slot}}</c>（Produce 既有模板）。</summary>
    public static TemplateDelimiters DoubleBrace { get; } = new("{{", "}}");

    /// <summary>定界符是否可用（前后缀均非空）。</summary>
    public bool IsValid => Prefix is { Length: > 0 } && Suffix is { Length: > 0 };

    /// <summary>校验并返回自身（配置错误在入口显式抛出，不静默退化）。</summary>
    public TemplateDelimiters EnsureValid()
    {
        if (!IsValid)
            throw new ArgumentException("模板定界符前后缀不得为空。", nameof(TemplateDelimiters));
        return this;
    }
}
