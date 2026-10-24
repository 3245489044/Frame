#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Abstractions;

/// <summary>
/// 宿主消息调度端口（F1）—— 消息边界只投递，不在 UI 线程执行取数/渲染。
/// 返回 true = 已接管（执行体在 worker 内）；false = 未接管（调用方沿用直接路径）。
/// </summary>
public interface IWebMessageScheduler
{
    /// <summary>尝试把一条页面消息投递到后台执行。</summary>
    /// <param name="payload">原始消息负载（用于提取合并键/优先级）。</param>
    /// <param name="execute">worker 内执行体（DOM 落地须经 IHtmlChannel 封送）。</param>
    bool TrySchedule(string payload, Func<CancellationToken, Task> execute);
}
