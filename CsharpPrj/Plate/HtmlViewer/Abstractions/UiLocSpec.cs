#nullable enable

using System.Collections.Generic;

namespace UiBackend.Abstractions;

/// <summary>
/// 位置呈现属性（loc → 模板/样式类/文案/常量/状态矩阵）。
/// <para>契约类型（非实现私有）：由声明端口提供，渲染层只读消费，禁自带第二份解析。</para>
/// </summary>
public sealed record UiLocSpec(
    string Kind,
    string? Value,
    string? OnMiss,
    IReadOnlyDictionary<string, string>? Matrix);
