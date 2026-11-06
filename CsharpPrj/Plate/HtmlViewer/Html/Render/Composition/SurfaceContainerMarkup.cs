#nullable enable

using System.Collections.Generic;

namespace UiBackend.Html;

/// <summary>载体无关的容器结构标记（Plate 层用的中性入参；不感知 Share/ResolvedSurface）。</summary>
/// <param name="Tag">标签名。</param>
/// <param name="Class">结构 + 材质类（已空格拼接）。</param>
/// <param name="Attributes">额外属性（data-*；键值已解析）。</param>
public sealed record SurfaceContainerMarkup(
    string Tag,
    string Class,
    IReadOnlyDictionary<string, string> Attributes);
