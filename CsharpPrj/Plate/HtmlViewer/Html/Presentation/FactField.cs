#nullable enable

using System;
using System.Globalization;

namespace UiBackend.Html.Presentation;

/// <summary>
/// 字段状态（三态受控词）：<see cref="Known"/> 有值 / <see cref="Unknown"/> 有声明面但缺值 /
/// <see cref="NotApplicable"/> 该视图不适用。与 <c>AX.NoSilentOutlet</c> 的 T/N/D/U 出口对应。
/// </summary>
public enum FieldState
{
    Known = 0,
    Unknown = 1,
    NotApplicable = 2,
}

/// <summary>
/// 一等呈现字段（**三态唯一载体**）：把"有没有值 / 值是什么 / 为什么没有"收敛为一个类型，
/// 消除 <c>int? + ?? 0 + ToString + "R"+n + "-"</c> 的多套表示。
/// <para>零领域词：字段名与取值由声明面（<see cref="PresentationContract"/>）给出。</para>
/// </summary>
public readonly record struct FactField<T>(FieldState State, T Value)
{
    /// <summary>有值。</summary>
    public bool HasValue => State == FieldState.Known;

    /// <summary>有值字段。</summary>
    public static FactField<T> Known(T value) => new(FieldState.Known, value);

    /// <summary>缺值（声明面适用但事实缺失）。</summary>
    public static FactField<T> Unknown() => new(FieldState.Unknown, default!);

    /// <summary>不适用（声明面判定不渲染）。</summary>
    public static FactField<T> NotApplicable() => new(FieldState.NotApplicable, default!);

    /// <summary>按可见性规则投影为模板值（不可见 / 不适用 ⇒ 空串，模板判空隐藏）。</summary>
    public string ToDisplay(FieldVisibility visibility)
        => visibility switch
        {
            FieldVisibility.Hidden => string.Empty,
            FieldVisibility.Placeholder => ToTextValue() is { Length: > 0 } text
                ? text
                : PresentationTokens.Unknown,
            _ => ToTextValue(),
        };

    private string ToTextValue()
        => State != FieldState.Known
            ? string.Empty
            : Value switch
            {
                null => string.Empty,
                string text => text,
                bool flag => flag ? "1" : string.Empty,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => Value!.ToString() ?? string.Empty,
            };
}

/// <summary>占位词单源（渲染层零领域文案；与未知词声明同值域）。</summary>
public static class PresentationTokens
{
    /// <summary>未知占位。</summary>
    public const string Unknown = "未知";

    /// <summary>空值占位（短横）。</summary>
    public const string Dash = "-";
}
