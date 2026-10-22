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

    /// <summary>
    /// 唯一阶段账本：块间数据 + 阶段结果 + 诊断 + 取消。
    /// 可由外部字典承载（Pipeline 适配层共享 SharedData）。
    /// </summary>
    public class BlockContext
    {
        private readonly List<BlockDiagnostic> _diagnostics = new();
        private readonly object _cancelLock = new();
        private CancellationToken _externalToken;
        private CancellationTokenSource? _cts;

        public BlockContext()
            : this(new ConcurrentDictionary<string, object?>(), default, null)
        {
        }

        public BlockContext(CancellationToken cancellationToken)
            : this(new ConcurrentDictionary<string, object?>(), cancellationToken, null)
        {
        }

        /// <summary>由适配层传入外部字典（如 PipelineContext.SharedData）以共享数据。</summary>
        public BlockContext(
            IDictionary<string, object?> data,
            CancellationToken cancellationToken = default,
            Action<BlockDiagnostic>? diagnosticSink = null)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            _externalToken = cancellationToken;
            DiagnosticSink = diagnosticSink;
        }

        public IDictionary<string, object?> Data { get; }

        /// <summary>唯一阶段账本（块名 → 诊断）。仅内核 / 适配层写入。</summary>
        public ConcurrentDictionary<string, BlockDiagnostic> StageResults { get; } =
            new(StringComparer.Ordinal);

        public IReadOnlyList<BlockDiagnostic> Diagnostics => _diagnostics;

        /// <summary>诊断写入旁路（Pipeline 适配层用于同步旧账本）。</summary>
        public Action<BlockDiagnostic>? DiagnosticSink { get; set; }

        public bool DiagnosticsEnabled { get; set; } = true;

        public CancellationToken CancellationToken
        {
            get => _cts?.Token ?? _externalToken;
            set
            {
                lock (_cancelLock)
                {
                    _externalToken = value;
                }
            }
        }

        public bool IsAborted => CancellationToken.IsCancellationRequested;

        public string? CancelReason { get; private set; }

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

        public bool IsSuccess(string blockName)
            => StageResults.TryGetValue(blockName, out var diagnostic)
               && diagnostic.Outcome is BlockOutcome.Success or BlockOutcome.Skipped;

        /// <summary>幂等取消：已取消直接返回，token 身份保持稳定。</summary>
        public void Cancel(string? reason = null)
        {
            lock (_cancelLock)
            {
                CancelReason ??= reason;

                if (_cts is not null)
                {
                    if (!_cts.IsCancellationRequested)
                        _cts.Cancel();
                    return;
                }

                if (_externalToken.IsCancellationRequested)
                    return;

                _cts = CancellationTokenSource.CreateLinkedTokenSource(_externalToken);
                _cts.Cancel();
            }
        }

        /// <summary>批处理子上下文：复制父数据引用，独立账本；批次结束由调用方汇总。</summary>
        public BlockContext CreateChild()
        {
            var childData = new ConcurrentDictionary<string, object?>();
            foreach (var kv in Data)
                childData[kv.Key] = kv.Value;

            return new BlockContext(childData, CancellationToken, DiagnosticSink)
            {
                DiagnosticsEnabled = DiagnosticsEnabled
            };
        }

        /// <summary>把子上下文诊断汇总到当前账本（批次汇总）。</summary>
        public void AbsorbDiagnostics(BlockContext child)
        {
            ArgumentNullException.ThrowIfNull(child);

            foreach (var diagnostic in child.Diagnostics)
                Record(diagnostic);
        }

        /// <summary>钩子失败独立记账（不翻转步骤 Outcome）。</summary>
        public void ReportHookFailure(string hookName, Exception exception)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(hookName);
            ArgumentNullException.ThrowIfNull(exception);

            Record(new BlockDiagnostic(
                hookName,
                false,
                TimeSpan.Zero,
                BlockStatus.Failed,
                $"钩子异常: {exception.Message}",
                exception)
            {
                Outcome = BlockOutcome.Failed,
                Fatal = false
            });
        }

        internal void Record(BlockDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);

            StageResults[diagnostic.BlockName] = diagnostic;

            if (!DiagnosticsEnabled)
                return;

            _diagnostics.Add(diagnostic);
            DiagnosticSink?.Invoke(diagnostic);
        }
    }
}
