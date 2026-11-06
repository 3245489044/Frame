#nullable enable

using System;
using System.Collections.Generic;

namespace Modules.CapabilityResolution;

/// <summary>一层图元的结构声明（领域无关）：图元名 + 叠放序 + 参数。</summary>
/// <param name="Name">层名（调用方命名空间内的唯一名）。</param>
/// <param name="Primitive">图元（fill/gradient/stroke/...；本内核不认识其语义）。</param>
/// <param name="Z">叠放序（唯一排序依据）。</param>
/// <param name="Params">参数表（值可为 token 名，由调用方提供换值函数）。</param>
/// <param name="Domain">该层值域（调用方分类；null = 由 resolver 的 domainClassifier 判定）。</param>
public sealed record LayerSpec(
    string Name,
    string Primitive,
    int Z,
    IReadOnlyDictionary<string, string> Params,
    string? Domain = null);
