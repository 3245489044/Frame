#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 材质**显式声明**的装饰能力。
/// <para>旧判据「层栈里有没有 backdrop」是代理推断：换一个不含 backdrop 的材质，装饰就静默消失，
/// 且没有任何记账。这里把意图写成声明，载体只按声明执行。</para>
/// </summary>
[Flags]
public enum SurfaceDecoration
{
    /// <summary>未声明任何装饰能力。</summary>
    None = 0,

    /// <summary>页场承接：本面**不铺不透明底**，背景由页面场（渐变 + 径向光 + 丝带）负责。</summary>
    PageField = 1 << 0,

    /// <summary>卡片 chrome：留白环 + 外发光（同一张页面场的切片）。</summary>
    Chrome = 1 << 1,

    /// <summary>虹彩环（材质层里的 specular/spectral）。</summary>
    Ring = 1 << 2,

    /// <summary>边缘彩带（3.6px 细窄带，只承载族内色相）。</summary>
    EdgeBand = 1 << 3,

    /// <summary>折射纹理（噪声叠加）。</summary>
    Texture = 1 << 4,
}

/// <summary>载体无关的绘制阶段（两载体共用顺序，载体只决定每阶段怎么落笔）。</summary>
public enum SurfacePaintStage
{
    /// <summary>页面场：渐变 + 径向光 + 丝带。</summary>
    PageField,

    /// <summary>面基底：fill / gradient / backdrop / stroke / shadow（静态层）。</summary>
    Base,

    /// <summary>面上的加法层：光斑 / 虹彩环（随光态变化的 specular 层）。</summary>
    Dynamic,

    /// <summary>卡片 chrome：留白环 + 外发光 + 彩带 + 纹理。</summary>
    Chrome,

    /// <summary>结构宿主的 1px 内衬边。</summary>
    FrameEdge,
}

/// <summary>控件在装饰序列里的角色（与载体无关）。</summary>
public enum SurfaceDecorScope
{
    /// <summary>普通面：只消费自身的装饰声明。</summary>
    Face,

    /// <summary>页场宿主：背景由页面场承载（分割容器 / 结构壳）。</summary>
    PageHost,
}

/// <summary>
/// 装饰决策的**唯一入口**：由材质声明 + 角色 + 页场可用性算出「这个槽位该画哪些阶段」。
/// <para>缺声明不静默：<see cref="Missing"/> 非空即表示该槽要求装饰却没有任何声明，调用方必须记账。</para>
/// </summary>
public sealed record SurfaceDecorPlan(
    SurfaceDecoration Declared,
    IReadOnlyList<SurfacePaintStage> Stages,
    string Missing)
{
    /// <summary>规范阶段顺序：页场 → 面基底 → 面上的加法层 → 卡片 chrome → 结构边。</summary>
    private static readonly SurfacePaintStage[] Canonical =
    {
        SurfacePaintStage.PageField,
        SurfacePaintStage.Base,
        SurfacePaintStage.Dynamic,
        SurfacePaintStage.Chrome,
        SurfacePaintStage.FrameEdge,
    };

    /// <summary>空计划（无材质）。</summary>
    public static readonly SurfaceDecorPlan None =
        new(SurfaceDecoration.None, Array.Empty<SurfacePaintStage>(), string.Empty);

    /// <summary>本计划是否执行该阶段。</summary>
    public bool Paints(SurfacePaintStage stage)
    {
        for (var i = 0; i < Stages.Count; i++)
        {
            if (Stages[i] == stage) return true;
        }

        return false;
    }

    /// <summary>本计划是否声明了该能力（chrome 内部特性也据此裁剪）。</summary>
    public bool Declares(SurfaceDecoration capability) => (Declared & capability) == capability;

    /// <summary>
    /// 解析某槽位的装饰计划。
    /// </summary>
    /// <param name="material">已解析材质（能力来自其 <c>decor</c> 声明）。</param>
    /// <param name="scope">控件角色。</param>
    /// <param name="pageFieldAvailable">页面场是否可用（档位未关闭装饰）。</param>
    public static SurfaceDecorPlan Resolve(
        ResolvedMaterial? material, SurfaceDecorScope scope, bool pageFieldAvailable)
    {
        if (material is null)
        {
            return new SurfaceDecorPlan(SurfaceDecoration.None, Array.Empty<SurfacePaintStage>(), "material-missing");
        }

        var declared = material.Decoration;
        var missing = scope == SurfaceDecorScope.PageHost && !material.DecorDeclared
            ? "decor-undeclared:" + material.MaterialId
            : string.Empty;

        var stages = new List<SurfacePaintStage>(Canonical.Length);
        foreach (var stage in Canonical)
        {
            var keep = stage switch
            {
                SurfacePaintStage.PageField => scope == SurfaceDecorScope.PageHost
                                               && pageFieldAvailable
                                               && (declared & SurfaceDecoration.PageField) != 0,
                SurfacePaintStage.Base => true,
                SurfacePaintStage.Dynamic => true,
                SurfacePaintStage.Chrome => pageFieldAvailable
                                            && (declared & SurfaceDecoration.Chrome) != 0,
                SurfacePaintStage.FrameEdge => scope == SurfaceDecorScope.PageHost,
                _ => false,
            };
            if (keep) stages.Add(stage);
        }

        return new SurfaceDecorPlan(declared, stages, missing);
    }

    /// <summary>解析材质 <c>decor</c> 列（逗号/分号/竖线/空白分隔，大小写不敏感）。</summary>
    /// <param name="text">声明原文。</param>
    /// <param name="unknown">未识别词（非空 ⇒ 声明写错，调用方应记账）。</param>
    public static SurfaceDecoration Parse(string? text, out IReadOnlyList<string> unknown)
    {
        var errors = new List<string>();
        unknown = errors;
        if (string.IsNullOrWhiteSpace(text)) return SurfaceDecoration.None;

        var result = SurfaceDecoration.None;
        foreach (var raw in text!.Split(new[] { ',', ';', '|', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.Trim().ToLowerInvariant())
            {
                case "pagefield":
                case "page_field":
                    result |= SurfaceDecoration.PageField;
                    break;
                case "chrome":
                    result |= SurfaceDecoration.Chrome;
                    break;
                case "ring":
                    result |= SurfaceDecoration.Ring;
                    break;
                case "edgeband":
                case "edge_band":
                    result |= SurfaceDecoration.EdgeBand;
                    break;
                case "texture":
                    result |= SurfaceDecoration.Texture;
                    break;
                default:
                    errors.Add(raw);
                    break;
            }
        }

        return result;
    }
}
