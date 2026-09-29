using System;
using System.Reactive.Subjects;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 三区"写入即触发"信号发布助手：提交后向 <see cref="Subject{T}"/> 发布 <see cref="ZoneWriteSignal"/>，
/// 异常仅记录不中断写入。收敛 <see cref="NodeZoneStore"/> 与 <see cref="InMemoryZoneStore"/> 重复的 Publish（SingleImplementation）。
/// </summary>
internal static class ZoneSignalPublisher
{
    public static void Publish(Subject<ZoneWriteSignal> subject, string source, ZoneWriteSignal signal)
    {
        // 无订阅者时不发布（避免产生"死信号"；reactive 关闭时内层 store 的 Writes 无人订阅则跳过，
        // reactive 开启时由 ReactiveZoneBridge 单独发布，消除同一写入的双重发布）。
        if (!subject.HasObservers) return;
        try { subject.OnNext(signal); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{source}] publish error: {ex.Message}");
        }
    }
}
