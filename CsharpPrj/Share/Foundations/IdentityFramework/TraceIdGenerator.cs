#nullable enable

using System;
using System.Globalization;
using System.Threading;

namespace Share.IdentityFramework;

/// <summary>
/// 追踪号生成器：一次操作一条可检索轨迹，形态 <c>"{前缀}-{序号:D8}"</c>，进程内单调递增。
/// <para>前缀由调用方给出（空白时回落到构造时指定的 fallbackName），
/// 框架不内置任何领域词；迁移 <c>RefreshTraceId</c> 时以 <c>fallbackName: "refresh"</c> 保持原值。</para>
/// </summary>
public sealed class TraceIdGenerator
{
    private readonly string _fallbackName;
    private readonly int _width;
    private readonly string _format;
    private long _seq;

    /// <param name="fallbackName">source 空白时使用的回落前缀（不得为空）。</param>
    /// <param name="width">序号补零宽度（1..19，默认 8）。</param>
    /// <param name="start">首个序号之前的值（默认 0，即首个输出为 1）。</param>
    public TraceIdGenerator(string fallbackName, int width = 8, long start = 0)
    {
        if (string.IsNullOrWhiteSpace(fallbackName))
            throw new ArgumentException("fallbackName 不得为空。", nameof(fallbackName));
        if (width is < 1 or > 19)
            throw new ArgumentOutOfRangeException(nameof(width), "序号宽度必须在 1..19 之间。");
        _fallbackName = fallbackName;
        _width = width;
        _format = "D" + width.ToString(CultureInfo.InvariantCulture);
        _seq = start;
    }

    /// <summary>产出单调追踪号：<c>{source}-{序号:Dwidth}</c>（source 空白时回落到 fallbackName）。</summary>
    public string New(string? source)
    {
        var name = string.IsNullOrWhiteSpace(source) ? _fallbackName : source!;
        var n = Interlocked.Increment(ref _seq);
        return name + "-" + n.ToString(_format, CultureInfo.InvariantCulture);
    }

    /// <summary>当前已分配的最大序号（诊断用）。</summary>
    public long Current => Interlocked.Read(ref _seq);

    /// <summary>序号补零宽度。</summary>
    public int Width => _width;
}
