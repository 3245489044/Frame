#nullable enable

using System;
using System.Collections.Generic;
using Share.Conversation.Presentation;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 渲染所需的**声明闭包**（单一入参，替代逐项参数扩散）。
/// <para>缺失项由各声明的 <c>Empty</c> 表达（可见降级），装配方只需给出能拿到的部分。</para>
/// </summary>
/// <param name="Ui">UI 个性化声明（token/slot/skin/part/carrier）。</param>
/// <param name="Map">载体结构映射（surface-map，由 ui_part 投影）。</param>
/// <param name="Fields">字段词表。</param>
/// <param name="Presentation">呈现类/事实映射声明。</param>
/// <param name="KindFields">kind → 需呈现的字段名（有序）。</param>
public sealed record SurfaceDeclarations(
    UiDeclaration Ui,
    SurfaceMapDeclaration Map,
    FieldDeclaration Fields,
    PresentationDeclaration Presentation,
    IReadOnlyDictionary<string, IReadOnlyList<string>> KindFields,
    ResolvedSurface? Resolved = null)
{
    /// <summary>空声明（全部缺失：渲染走可见降级，不伪造）。</summary>
    public static readonly SurfaceDeclarations Empty = new(
        UiDeclaration.Empty,
        SurfaceMapDeclaration.Empty,
        FieldDeclaration.Empty,
        PresentationDeclaration.Empty,
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    /// <summary>合成用声明视图。</summary>
    public FrameComposerOptions ComposerOptions => new(Presentation, Fields, KindFields);
}