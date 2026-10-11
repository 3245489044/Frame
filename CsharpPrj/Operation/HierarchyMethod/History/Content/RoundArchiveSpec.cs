using System;
using System.Collections.Generic;

namespace Operation.HierarchyMethod.History.Content;

/// <summary>
/// 原文归档的**域差异参数**（宿主注入；框架本体零域字面量）。
/// </summary>
/// <param name="NodePlacementId">归档单行位置 id（存储键模板求值用，如 <c>archive-node</c>）。</param>
/// <param name="WriterId">归档域写入者 id。</param>
/// <param name="NodePlacementIds">归档单行物化位置 id 集。</param>
/// <param name="RoundPlacementIds">轮次键位置 id 集（新形态在前，legacy 在后；顺序 = 读回退优先级，用于 legacy 读取与轮次反解）。</param>
/// <param name="Kind">归档内容类别。</param>
/// <param name="Ordinal">归档条目序数（每轮恰一条 ⇒ 恒 1）。</param>
/// <param name="NodeType">归档库行判别（列举归档行用）。</param>
/// <param name="ContainerPlaceholder">身份模板容器占位符名。</param>
/// <param name="RoundPlaceholder">身份模板轮次占位符名。</param>
/// <param name="IsArchiveKey">归档键族判据。</param>
public sealed record RoundArchiveSpec(
    string NodePlacementId,
    string WriterId,
    IReadOnlyList<string> NodePlacementIds,
    IReadOnlyList<string> RoundPlacementIds,
    string Kind,
    long Ordinal,
    string NodeType,
    string ContainerPlaceholder,
    string RoundPlaceholder,
    Func<string, bool> IsArchiveKey);
