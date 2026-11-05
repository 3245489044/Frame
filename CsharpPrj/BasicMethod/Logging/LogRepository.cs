
using System.Collections.Concurrent;
using Modules.Compute.Formula;

namespace Common.Diagnostics
{

    public enum LogOutputMode
    {
        Database = 0,

        Console = 1
    }

    public static class LogRepository
    {
        public static int MaxEntries { get; set; } = 10000;

        public static bool Enabled { get; set; } = true;

        public static LogOutputMode OutputMode { get; set; } = LogOutputMode.Console;

        public static event Action<LogEntry>? LogWritten;

        private static readonly ConcurrentQueue<LogEntry> _logs = new();

        private static readonly ConcurrentDictionary<LogCategory, List<LogEntry>> _categoryIndex = new();

        private static readonly ConcurrentDictionary<string, List<LogEntry>> _levelIndex = new();

        private static readonly ConcurrentDictionary<string, List<LogEntry>> _moduleIndex = new();

        private static readonly object _trimLock = new();

        private static long _nextSequence = 0;

        /// <summary>去重窗口容量（有界；长跑进程不得单调增长）。</summary>
        public const int DedupCapacity = 4096;

        /// <summary>去重窗口条目数（诊断用）。</summary>
        public static int DedupCacheCount => _writeDedupCache.Count;

        private static readonly BoundedCache<string, long> _writeDedupCache = new(DedupCapacity);

        public static void Write(string level, string category, string module, string message)
        {
            LogCategory parsedCategory;
            if (!Enum.TryParse<LogCategory>(category, ignoreCase: true, out parsedCategory))
            {
                if (LoggingSetup.ResolveEnumName != null)
                {
                    parsedCategory = LoggingSetup.ResolveEnumName(category);
                }
                else
                {
                    parsedCategory = LogCategory.System;
                }
            }

            // 条目创建经工厂端口（默认 DefaultEntryFactory；批次打标适配器由上层注入，DG-4.1 单源）。
            var entry = LoggingSetup.EntryFactory.CreateEntry(
                (level?.ToUpperInvariant()) ?? "INFO", parsedCategory, module, message,
                exception: null, callerFile: null, callerLine: 0, callerMember: null);
            entry.CategoryString = category;
            Write(entry);
        }

        public static void Write(LogEntry entry)
        {
            if (!Enabled) return;

            var formatted = $"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level}] [{entry.Module}] {entry.Message}";

            // 去重键必须覆盖被去重维度（level/module/message）：仅用正文会让不同模块同文案被误抑制。
            var dedupKey = $"{entry.Level}|{entry.Module}|{entry.Message}";
            var nowMs = Environment.TickCount64;
            var suppressed = _writeDedupCache.TryGet(dedupKey, out var lastMs)
                             && (nowMs - lastMs) < 100;

            // E-01：去重只抑制「打印/广播」，不抑制留档（重复本身必须可审计）。
            if (suppressed)
            {
                entry.Extensions["SuppressedDuplicate"] = true;
            }
            else
            {
                _writeDedupCache.Set(dedupKey, nowMs);
            }

            // 过滤一次、两处共用：Console 打印与 LogWritten 广播必须同口径（否则终端与控制台内容漂移）。
            var printable = LogFilterLoader.ShouldPrint(entry);

            if (OutputMode == LogOutputMode.Console)
            {
                if (printable && !suppressed)
                {
                    LogCollapser.Write(formatted);
                    // Console 模式同样广播：GUI 默认即 Console 模式，不广播则进程内订阅者（UI 终端）永收不到日志。
                    LogWritten?.Invoke(entry);
                }

                // Console 只决定「是否打印」，不决定「是否留档」：不入内存队列会让批次日志恒为空（DG-4.1）。
                Record(entry);
                return;
            }

            if (entry.Level is "Error" or "Warning" && printable && !suppressed)
            {
                LogCollapser.Write(formatted);
            }

            Record(entry);

            if (!suppressed)
            {
                LogWritten?.Invoke(entry);
            }
        }

        /// <summary>入队 + 建索引 + 有界裁剪（唯一写入路径；两种输出模式共用）。</summary>
        private static void Record(LogEntry entry)
        {
            entry.SequenceNumber = Interlocked.Increment(ref _nextSequence);

            _logs.Enqueue(entry);

            AddToIndex(_categoryIndex, entry.Category, entry);

            AddToIndex(_levelIndex, entry.Level, entry);

            if (!string.IsNullOrEmpty(entry.Module))
            {
                AddToIndex(_moduleIndex, entry.Module, entry);
            }

            if (_logs.Count > MaxEntries)
            {
                TrimExcess();
            }
        }

        public static IEnumerable<LogEntry> GetByCategory(LogCategory category)
        {
            return _categoryIndex
                .Where(kvp => category.HasFlag(kvp.Key))
                .SelectMany(kvp => kvp.Value)
                .OrderBy(e => e.Timestamp);
        }

        public static IEnumerable<LogEntry> GetByLevel(string level)
        {
            return _levelIndex.TryGetValue(level, out var list)
                ? list.OrderBy(e => e.Timestamp)
                : Enumerable.Empty<LogEntry>();
        }

        public static IEnumerable<LogEntry> GetByModule(string module)
        {
            return _moduleIndex.TryGetValue(module, out var list)
                ? list.OrderBy(e => e.Timestamp)
                : Enumerable.Empty<LogEntry>();
        }

        /// <summary>
        /// 仓库查询（唯一筛选轴声明）：<paramref name="category"/> 按枚举判定（<see cref="LogCategory"/>）；
        /// <see cref="LogEntry.CategoryString"/> 仅用于展示与打印过滤（<c>LogFilterLoader</c>），不参与本查询。
        /// </summary>
        public static IEnumerable<LogEntry> Query(LogCategory? category = null, string? level = null, string? module = null)
        {
            IEnumerable<LogEntry> result = _logs;

            if (category.HasValue && category.Value != LogCategory.None)
            {
                result = result.Where(e => category.Value.HasFlag(e.Category));
            }

            if (!string.IsNullOrEmpty(level))
            {
                result = result.Where(e => e.Level == level);
            }

            if (!string.IsNullOrEmpty(module))
            {
                result = result.Where(e => e.Module == module);
            }

            return result.OrderBy(e => e.Timestamp);
        }

        public static IEnumerable<LogEntry> Query(Func<LogEntry, bool> predicate)
        {
            return _logs.Where(predicate).OrderBy(e => e.Timestamp);
        }

        public static IEnumerable<LogEntry> GetAll()
        {
            return _logs.OrderBy(e => e.Timestamp);
        }

        public static IEnumerable<LogEntry> GetNewerThan(long sequenceNumber)
        {
            return _logs.Where(e => e.SequenceNumber > sequenceNumber)
                        .OrderBy(e => e.SequenceNumber);
        }

        public static long GetMaxSequence()
        {
            return Interlocked.Read(ref _nextSequence);
        }

        public static int Count => _logs.Count;

        public static void Reset()
        {
            LogCollapser.Flush();
            _logs.Clear();
            _categoryIndex.Clear();
            _levelIndex.Clear();
            _moduleIndex.Clear();
            _writeDedupCache.Clear();
            Interlocked.Exchange(ref _nextSequence, 0);
        }

        private static void AddToIndex<T>(ConcurrentDictionary<T, List<LogEntry>> index, T key, LogEntry entry) where T : notnull
        {
            index.AddOrUpdate(
                key,
                _ => new List<LogEntry> { entry },
                (_, list) =>
                {
                    lock (list)
                    {
                        list.Add(entry);
                    }
                    return list;
                });
        }

        private static void TrimExcess()
        {
            lock (_trimLock)
            {
                var removeCount = Math.Max(1, MaxEntries / 10);
                for (int i = 0; i < removeCount; i++)
                {
                    _logs.TryDequeue(out _);
                }
                RebuildIndex();
            }
        }

        private static void RebuildIndex()
        {
            _categoryIndex.Clear();
            _levelIndex.Clear();
            _moduleIndex.Clear();

            foreach (var entry in _logs)
            {
                AddToIndex(_categoryIndex, entry.Category, entry);
                AddToIndex(_levelIndex, entry.Level, entry);
                if (!string.IsNullOrEmpty(entry.Module))
                {
                    AddToIndex(_moduleIndex, entry.Module, entry);
                }
            }
        }
    }
}
