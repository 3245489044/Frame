using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.PortContexts;

/// <summary>出口字段视图（分类 + 稳定度 + 键值；与内核字段同构，供出口侧消费）。</summary>
public sealed record PortContextFieldView(string Class, string Volatility, string Key, string? Value);

/// <summary>出口成员视图（成员键 + 字段 + 空因 + 截断标记）。</summary>
public sealed record PortContextMemberView(
    string MemberKey,
    IReadOnlyList<PortContextFieldView> Fields,
    string? EmptyCode,
    bool Truncated);

/// <summary>出口模型（三出口共用同一形状；字段集 = <see cref="FieldKeys"/>）。</summary>
public sealed record PortContextViewModel(
    string ContainerKey,
    IReadOnlyList<PortContextMemberView> Members,
    IReadOnlyList<PortContextFieldView> Fields,
    string? EmptyCode,
    bool Synthetic,
    bool Truncated,
    IReadOnlyList<string> FieldKeys);

/// <summary>出口投影器契约：只读同一 <see cref="PortContext"/>，字段集必须相等（UPC-I2）。</summary>
public interface IPortContextConsumer
{
    /// <summary>出口 Id（ui / prompt / diagnostics）。</summary>
    string Outlet { get; }

    /// <summary>投影为出口模型（只读；不得再取数）。</summary>
    /// <param name="context">一次装配结果。</param>
    PortContextViewModel Project(PortContext context);
}

/// <summary>出口投影唯一实现（三出口共用；禁各自裁剪字段）。</summary>
public static class PortContextProjection
{
    /// <summary>上下文 → 出口模型。</summary>
    /// <param name="context">一次装配结果。</param>
    public static PortContextViewModel Project(PortContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var members = new List<PortContextMemberView>(context.Members.Count);
        foreach (var member in context.Members)
        {
            var fields = member.StateFields
                .Concat(member.WindowItems.Select(static item => new PortContextField(
                    PortContextFieldClasses.Window, PortContextVolatility.Stream, "window", item.Text)))
                .Select(ToView)
                .ToArray();
            members.Add(new PortContextMemberView(member.MemberKey, fields, member.EmptyCode, member.Truncated));
        }

        var allFields = members.SelectMany(static member => member.Fields).ToArray();
        var fieldKeys = allFields
            .Select(static field => field.Key)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();

        return new PortContextViewModel(
            context.ContainerKey,
            members,
            allFields,
            context.EmptyCode,
            context.Synthetic,
            context.Truncated,
            fieldKeys);
    }

    private static PortContextFieldView ToView(PortContextField field)
        => new(field.Class, field.Volatility, field.Key, field.Value);
}

/// <summary>UI 出口（卡片正文档）。</summary>
public sealed class PortContextUiConsumer : IPortContextConsumer
{
    /// <summary>出口 Id。</summary>
    public const string OutletId = "ui";

    /// <inheritdoc />
    public string Outlet => OutletId;

    /// <inheritdoc />
    public PortContextViewModel Project(PortContext context) => PortContextProjection.Project(context);
}

/// <summary>Prompt 出口（task.runtime 段）。</summary>
public sealed class PortContextPromptConsumer : IPortContextConsumer
{
    /// <summary>出口 Id。</summary>
    public const string OutletId = "prompt";

    /// <inheritdoc />
    public string Outlet => OutletId;

    /// <inheritdoc />
    public PortContextViewModel Project(PortContext context) => PortContextProjection.Project(context);
}

/// <summary>诊断出口（徽章/观测）。</summary>
public sealed class PortContextDiagnosticsConsumer : IPortContextConsumer
{
    /// <summary>出口 Id。</summary>
    public const string OutletId = "diagnostics";

    /// <inheritdoc />
    public string Outlet => OutletId;

    /// <inheritdoc />
    public PortContextViewModel Project(PortContext context) => PortContextProjection.Project(context);
}

/// <summary>出口同形断言（UPC-I2）：字段集相等；差异须显式登记 shape-exception（本内核不提供默认例外）。</summary>
public static class PortContextShape
{
    /// <summary>字段集是否全等（顺序无关；集合语义）。</summary>
    /// <param name="views">各出口模型。</param>
    public static bool FieldsEqual(params PortContextViewModel[] views)
    {
        if (views is null || views.Length < 2) return true;
        var baseline = views[0].FieldKeys;
        return views.Skip(1).All(view => baseline.SequenceEqual(view.FieldKeys, StringComparer.Ordinal));
    }

    /// <summary>全部受控空因码（门禁/单测遍历；单一来源 = <see cref="PortContextEmptyCodes.Codes"/>）。</summary>
    public static IReadOnlyList<string> EmptyCodes => PortContextEmptyCodes.Codes.All;
}
