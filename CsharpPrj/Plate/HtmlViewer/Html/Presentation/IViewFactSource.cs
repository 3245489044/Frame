#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Html.Presentation;

/// <summary>
/// 视图事实源端口（IoC：Frame 定义抽象，领域提供实现）。
/// <para>领域只回答"字段值是什么"（三态），不参与"是否显示 / 如何格式化 / 用哪个模板"——
/// 后者归 <see cref="PresentationContract"/> 与模板声明。</para>
/// </summary>
public interface IViewFactSource
{
    /// <summary>按视图 + 选择键取字段事实（未声明字段可缺省；缺省 = <see cref="FieldState.Unknown"/>）。</summary>
    Task<IReadOnlyDictionary<string, FactField<object?>>> GetAsync(
        string viewKey,
        string selectionKey,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken);
}

/// <summary>事实集构造助手（单点，避免各实现手写字典字面量）。</summary>
public static class ViewFacts
{
    /// <summary>新建事实集。</summary>
    public static Dictionary<string, FactField<object?>> Create()
        => new(StringComparer.OrdinalIgnoreCase);

    /// <summary>写入有值事实（null / 空白 ⇒ Unknown，不伪造）。</summary>
    public static void Put(Dictionary<string, FactField<object?>> facts, string field, object? value)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (string.IsNullOrWhiteSpace(field)) return;
        facts[field] = value is null || (value is string text && string.IsNullOrWhiteSpace(text))
            ? FactField<object?>.Unknown()
            : FactField<object?>.Known(value);
    }

    /// <summary>写入不适用事实。</summary>
    public static void PutNotApplicable(Dictionary<string, FactField<object?>> facts, string field)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (string.IsNullOrWhiteSpace(field)) return;
        facts[field] = FactField<object?>.NotApplicable();
    }
}
