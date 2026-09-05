
#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.Shared.Framework;
using Share.EventBindingOrchestrator.Abstractions;
using Share.EventBindingOrchestrator.Execution;

namespace FrontEndDriven.Eventing;

public interface IEvent
{
    string EventType { get; }

    string? Source { get; }

    string Scope { get; }

    object? Payload { get; }

    DateTime Timestamp { get; }

    string? ScopePath { get; }
}

public static class EventTypeNames
{
    public const string ConfigLoading = "lifecycle.ConfigLoading";
    public const string ConfigLoaded = "lifecycle.ConfigLoaded";
    public const string ConfigDestroying = "lifecycle.ConfigDestroying";
    public const string ConfigDestroyed = "lifecycle.ConfigDestroyed";

    public const string FunctionExecuted = "function.Executed";
}

public sealed record ConfigLoadPayload(string PhaseId, string ConfigPath, int NodeCount = 0);

public sealed record ConfigDestroyPayload(string Scope);

public sealed record FunctionExecutedPayload(
    string FunctionName,
    bool IsSuccess,
    string? Error,
    long ElapsedMs,
    string Scope = "",
    // 阶段2.2：函数渲染副作用元数据（替代渲染桥按函数名前缀推断；见 RenderingAffinity）
    RenderingAffinity RenderingAffinity = RenderingAffinity.None);

public sealed record PlatformEvent(
    string EventType,
    string Scope,
    object? Payload = null,
    string? Source = null,
    string? ScopePath = null) : IEvent
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static PlatformEvent Loading(string phaseId, string scope, string configPath)
        => new(EventTypeNames.ConfigLoading, scope, new ConfigLoadPayload(phaseId, configPath), phaseId);

    public static PlatformEvent Loaded(string phaseId, string scope, string configPath, int nodeCount = 0)
        => new(EventTypeNames.ConfigLoaded, scope, new ConfigLoadPayload(phaseId, configPath, nodeCount), phaseId);

    public static PlatformEvent Destroying(string scope)
        => new(EventTypeNames.ConfigDestroying, scope, new ConfigDestroyPayload(scope));

    public static PlatformEvent Destroyed(string scope)
        => new(EventTypeNames.ConfigDestroyed, scope, new ConfigDestroyPayload(scope));

    public static PlatformEvent FunctionDone(
        string functionName,
        bool isSuccess,
        string? error,
        long elapsedMs,
        string scope = "",
        RenderingAffinity renderingAffinity = RenderingAffinity.None)
        => new(EventTypeNames.FunctionExecuted, scope,
            new FunctionExecutedPayload(functionName, isSuccess, error, elapsedMs, scope, renderingAffinity),
            functionName);
}

public sealed class EventPlatform : IEventPlatform, IDisposable
{
    private readonly EventBus<IEvent> _bus;
    private bool _disposed;

    public EventPlatform()
    {
        // 订阅存储 + 分发 + 生命周期收敛到通用 EventBus<IEvent>；订阅者异常经日志门面记录，不中断其它订阅者。
        _bus = new EventBus<IEvent>(
            eventTypeSelector: evt => evt.EventType,
            handlerError: (evt, ex) => FrontEndDrivenLog.Log("EventPlatform_handler_error",
                new { eventType = evt.EventType, source = evt.Source, error = ex.Message }));
    }

    public IDisposable Subscribe(string eventType, Action<IEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _bus.Subscribe(eventType, handler);
    }

    public IDisposable SubscribeAsync(string eventType, Func<IEvent, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfDisposed();

        return _bus.SubscribeAsync(eventType, handler);
    }

    public IDisposable SubscribeOfType<TEvent>(string eventType, Action<TEvent> handler)
        where TEvent : class, IEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _bus.SubscribeOfType(eventType, handler);
    }

    public async Task PublishAsync(IEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ThrowIfDisposed();

        await _bus.PublishAsync(evt).ConfigureAwait(false);
    }

    public void Publish(IEvent evt)
        => PublishAsync(evt).GetAwaiter().GetResult();

    public Task PublishLifecycleAsync(IEvent evt) => PublishAsync(evt);

    public async Task<FunctionCallFacadeResult> InvokeFunctionAsync(
        string functionName,
        Dictionary<string, object?>? parameters = null,
        string? source = null,
        int timeoutMs = 30_000,
        string scope = "")
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(functionName))
        {
            return new FunctionCallFacadeResult(false, null, "函数名称为空", 0);
        }

        var request = new FunctionCallRequest
        {
            FunctionName = functionName,
            Parameters = parameters,
            Source = source is null ? FunctionCallSource.ExternalPort : ParseSource(source),
            Mode = ExecutionMode.Auto,
            TimeoutMs = timeoutMs
        };

        // P5/RD5：消除双层线程包装（原 Task.Run 包内部 RunSync 同步等待）。
        // 直接 await ExecuteAsync——async 方法本身不阻塞调用线程，background 差异由调用方 Task.Run 承担。
        var result = await FunctionCallFacade.ExecuteAsync(request)
            .ConfigureAwait(false);

        // 缺陷2 修复：函数执行事件携带 scope（默认空=全量向后兼容），
        // FunctionExecutedRenderBridge 可据此限定重渲染作用域。
        // 阶段2.2：事件携带 RenderingAffinity（执行门面解析），渲染桥不再按函数名前缀推断。
        await PublishAsync(PlatformEvent.FunctionDone(
            functionName, result.IsSuccess, result.ErrorMessage, result.ElapsedMs, scope, result.RenderingAffinity))
            .ConfigureAwait(false);

        // R:3.4 —— FunctionCallFacade.ExecuteAsync 已直接返回 FunctionCallFacadeResult（外部契约），
        // 消除原 BackEndDriven.FunctionCallResult → FunctionCallFacadeResult 的重复投影。
        return result;
    }

    public FunctionCallFacadeResult InvokeFunction(
        string functionName,
        Dictionary<string, object?>? parameters = null,
        string? source = null,
        int timeoutMs = 30_000,
        string scope = "")
        => InvokeFunctionAsync(functionName, parameters, source, timeoutMs, scope).GetAwaiter().GetResult();

    public int SubscriberCount(string eventType)
        => _bus.SubscriberCount(eventType);

    public bool HasSubscribers(string eventType)
        => _bus.HasSubscribers(eventType);

    private static FunctionCallSource ParseSource(string source)
        => Enum.TryParse<FunctionCallSource>(source, true, out var parsed) ? parsed : FunctionCallSource.Other;

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EventPlatform));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _bus.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
