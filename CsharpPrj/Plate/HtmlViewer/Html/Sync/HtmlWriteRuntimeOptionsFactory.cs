#nullable enable

using System;

namespace UiBackend.Html;

/// <summary>
/// HTML 写运行时预算与遥测的**声明单源**（通用件）—— 帧预算/批量/渐进面等参数在此声明一次，
/// 装配代码不再散落魔法数；诊断串与参数同源产出，开关可见化不靠读源码。
/// </summary>
public static class HtmlWriteRuntimeOptionsFactory
{
    /// <summary>框架默认写运行时参数（首挂载/刷新按 壳→结构→内容→装饰 落地；预算压力先丢装饰）。</summary>
    public static UiWriteRuntimeOptions CreateDefault()
        => new()
        {
            FrameBudget = true,
            MaxOpsPerFlush = 64,
            MaxBytesPerFlush = 1536 * 1024,
            MaxMsPerFlush = 120,
            // S2 转正：节批量写入口（一拍一 batch）；关掉即回旧逐目标路径（灰度回退）。
            UseSectionWriter = true,
            MaxNodesPerBeat = 1500,
            MaxNodesPerBeatBackground = 500,
            BulkNodeThreshold = 800,
            BulkRatioThreshold = 0.5,
            BulkReplaceEnabled = true,
            StreamDroppable = true,
            ProgressiveSurface = true,
        };

    /// <summary>开关可见化描述（单发诊断用）：字段集合与参数声明同源。</summary>
    public static string Describe(UiWriteRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return $"frameBudget={options.FrameBudget}, maxOps={options.MaxOpsPerFlush}, " +
               $"maxBytes={options.MaxBytesPerFlush}, maxMs={options.MaxMsPerFlush}, " +
               $"interactiveFastPath={options.InteractiveFastPath}, maxMsCap={options.MaxMsPerFlush}, " +
               $"sectionWriter={options.UseSectionWriter}, maxNodesPerBeat={options.MaxNodesPerBeat}, " +
               $"bulkNode={options.BulkNodeThreshold}, bulkRatio={options.BulkRatioThreshold:F2}";
    }
}
