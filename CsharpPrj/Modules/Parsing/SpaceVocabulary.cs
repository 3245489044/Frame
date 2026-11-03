using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.Parsing;

/// <summary>
/// 空间词表取值器（纯逻辑单源）：坐标名序列的规范化与物化。
/// <para>平台编译产物（Usharp 等）由宿主适配为坐标名序列后调用；框架不认平台类型。</para>
/// </summary>
public static class SpaceVocabulary
{
    /// <summary>取某空间的坐标名序（未声明/空 ⇒ 空表）。</summary>
    public static IReadOnlyList<string> AxisNames(IEnumerable<string>? axisNames)
        => axisNames is null ? Array.Empty<string>() : axisNames.ToArray();
}
