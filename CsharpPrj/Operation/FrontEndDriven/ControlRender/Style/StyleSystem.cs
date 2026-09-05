﻿
using System;
using ECS.Core;
using FrontEndDriven.Shared.Framework;
using TConfig.ECS.Components;
using FrontEndDriven.ECS.Components;

namespace FrontEndDriven.ECS.Systems;

/// <summary>样式合并系统：处理带 PendingStyleComponent 的实体（合并样式 → 写 MergedStyleComponent + StyleAppliedComponent）。
/// 扫描/移除标记循环收敛到通用 <see cref="PendingEcsSystem{TMarker}"/>。</summary>
public class StyleSystem : PendingEcsSystem<PendingStyleComponent>
{
    public event Func<string, (ColorStyleComponent? color, FontStyleComponent? font)>? StyleResolveRequested;

    public StyleSystem(EntityManager entityManager)
        : base(entityManager)
    {
    }

    protected override void Process(Entity entity)
    {
        var merged = MergeStyles(entity);

        EntityManager.AddOrSetComponent(entity, merged);
        EntityManager.AddComponent(entity, new StyleAppliedComponent());
    }

    private MergedStyleComponent MergeStyles(Entity entity)
    {
        var merged = new MergedStyleComponent
        {
            BackColor = "#F0F0F0",
            ForeColor = "#000000",
            BorderColor = "#CCCCCC",
            BorderWidth = 1,
            FontFamily = FrontEndDrivenConstants.DefaultFontFamily,
            FontSize = FrontEndDrivenConstants.DefaultFontSize,
            Bold = false,
            Italic = false,
            Underline = false
        };

        if (EntityManager.TryGetComponent<StyleRefComponent>(entity, out var styleRef) &&
            !string.IsNullOrEmpty(styleRef.StyleRef))
        {
            var (color, font) = StyleResolveRequested?.Invoke(styleRef.StyleRef) ?? (null, null);
            if (color.HasValue)
            {
                merged.BackColor = color.Value.BackColor;
                merged.ForeColor = color.Value.ForeColor;
                merged.BorderColor = color.Value.BorderColor;
                merged.BorderWidth = color.Value.BorderWidth;
            }
            if (font.HasValue)
            {
                merged.FontFamily = font.Value.FontFamily;
                merged.FontSize = font.Value.FontSize;
                merged.Bold = font.Value.Bold;
                merged.Italic = font.Value.Italic;
            }
        }

        if (EntityManager.TryGetComponent<ColorStyleComponent>(entity, out var colorStyle))
        {
            if (!string.IsNullOrEmpty(colorStyle.BackColor))
                merged.BackColor = colorStyle.BackColor;
            if (!string.IsNullOrEmpty(colorStyle.ForeColor))
                merged.ForeColor = colorStyle.ForeColor;
            if (!string.IsNullOrEmpty(colorStyle.BorderColor))
                merged.BorderColor = colorStyle.BorderColor;
            if (colorStyle.BorderWidth > 0)
                merged.BorderWidth = colorStyle.BorderWidth;
        }

        if (EntityManager.TryGetComponent<FontStyleComponent>(entity, out var fontStyle))
        {
            if (!string.IsNullOrEmpty(fontStyle.FontFamily))
                merged.FontFamily = fontStyle.FontFamily;
            if (fontStyle.FontSize > 0)
                merged.FontSize = fontStyle.FontSize;
            merged.Bold = fontStyle.Bold;
            merged.Italic = fontStyle.Italic;
            merged.Underline = fontStyle.Underline;
        }

        return merged;
    }
}
