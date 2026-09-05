using System;
using System.Collections.Generic;
using ExecutionCenter.Abstractions;
using ExecutionCenter.Messages;

namespace ExecutionCenter.Matrix;

/// <summary>
/// 集中矩阵事件聚拢点：所有 UI 事件/命令统一经此汇入矩阵执行后端（ITaskExecutorPool），
/// 发射即返回（fire-and-forget）、绝不阻塞调用线程、绝不在 UI 线程同步等待异步结果——
/// 从架构上消除「同步异步死锁」。
/// 线程安全：ITaskExecutorPool.SubmitAsync 入队（ConcurrentQueue）线程安全，任意线程可调。
/// 与 EventPlatform 分工：本类管「事件→矩阵任务发射」，EventPlatform 管「任务结果事件广播」。
/// </summary>
public sealed class MatrixEventSink : IDisposable
{
    private readonly ITaskExecutorPool _pool;
    private bool _disposed;

    public MatrixEventSink(ITaskExecutorPool pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    /// <summary>统一发射入口：任意线程可调，立即返回，不等待执行结果。</summary>
    public void Emit(ITaskMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ThrowIfDisposed();

        try
        {
            _ = _pool.SubmitAsync(message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MatrixEventSink] Emit 失败: {ex.Message}");
        }
    }


    /// <summary>便捷发射：执行函数。</summary>
    public void EmitFunction(string functionName, Dictionary<string, object>? parameters = null)
    {
        Emit(new ExecuteFunctionMessage
        {
            FunctionName = functionName,
            Parameters = parameters
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MatrixEventSink));
    }
}