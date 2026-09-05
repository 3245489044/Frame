
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace BasicMethod.Block
{

    public enum BlockStatus
    {
        Pending,

        Running,

        Success,

        Failed,

        Skipped,

        Aborted
    }


    public record BlockResult(
        bool IsSuccess,
        string? Message = null,
        Exception? Exception = null
    )
    {
        public static BlockResult Ok(string? message = null)
            => new(true, message ?? "执行成功");

        public static BlockResult Fail(string? message = null, Exception? exception = null)
            => new(false, message ?? "执行失败", exception);

        public static BlockResult Skip(string? message = null)
            => new(true, message ?? "已跳过");

        public static BlockResult Abort(string? message = null)
            => new(false, message ?? "已中止");
    }


    public record BlockDiagnostic(
        string BlockName,
        bool Success,
        TimeSpan Duration,
        BlockStatus Status,
        string? Message = null,
        Exception? Exception = null
    );


    public class BlockContext
    {
        public ConcurrentDictionary<string, object?> Data { get; } = new();

        public List<BlockDiagnostic> Diagnostics { get; } = new();

        public CancellationToken CancellationToken { get; set; }

        public bool IsAborted => CancellationToken.IsCancellationRequested;

        public T? Get<T>(string key)
        {
            if (Data.TryGetValue(key, out var value) && value is T typedValue)
                return typedValue;
            return default;
        }

        public void Set<T>(string key, T value)
        {
            Data[key] = value;
        }

        public bool TryGet<T>(string key, out T? value)
        {
            if (Data.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        internal void AddDiagnostic(BlockDiagnostic diagnostic)
        {
            Diagnostics.Add(diagnostic);
        }
    }


    public record PipelineResult(
        bool OverallSuccess,
        BlockContext Context,
        IReadOnlyList<BlockDiagnostic> Diagnostics
    )
    {
        public int SuccessCount { get; init; }

        public int FailedCount { get; init; }

        public int SkippedCount { get; init; }

        public TimeSpan TotalDuration { get; init; }
    }
}
