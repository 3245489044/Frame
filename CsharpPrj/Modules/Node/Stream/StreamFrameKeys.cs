using System;

namespace Modules.Node.Stream;

/// <summary>
/// Stream **帧级显示键**（通用；域无关）：<c>{prefix}{task}.f{seq}</c> —— **仅读侧内存合成**，
/// 供聚拢引擎组内保序（<c>NodeKeyOrdinalComparer</c> 数值序 = <c>seq</c>）；
/// <b>不落库、不进声明/载体注册表</b>（非持久位置，禁当位置键使用）。
/// </summary>
public static class StreamFrameKeys
{
    /// <summary>帧序号段前缀（<c>f{seq}</c>）。</summary>
    public const string SeqSegmentPrefix = "f";

    /// <summary>合成帧级显示键（= 族前缀 + 分区键 + <c>.f{seq}</c>）。</summary>
    /// <param name="familyPrefix">键族前缀（如 <c>obs.</c> / <c>aux.</c>）。</param>
    /// <param name="partitionKey">分区键（容器）。</param>
    /// <param name="seq">到达序。</param>
    public static string DisplayKey(string familyPrefix, string partitionKey, long seq)
    {
        ArgumentNullException.ThrowIfNull(familyPrefix);
        ArgumentNullException.ThrowIfNull(partitionKey);
        return familyPrefix + partitionKey + "." + SeqSegmentPrefix + seq;
    }
}
