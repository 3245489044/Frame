
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Threading;
using Modules.Common;
using Modules.Common.Caching;
using SkeletonFlow;
using TConfig;

namespace Modules.Pipeline
{


    public enum PipelineStage
    {
        Initialize = 0,
        Load = 1,
        Validate = 2,
        Process = 3,
        Map = 4,
        Transform = 5,
        Save = 6,
        Cleanup = 7,
        Complete = 8
    }


    public enum PipelineStatus
    {
        Idle = 0,
        Running = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4
    }

    public static class PipelineStageExtensions
    {
        private static readonly Dictionary<PipelineStage, string> StageNames = new()
        {
            [PipelineStage.Initialize] = "初始化",
            [PipelineStage.Load] = "加载",
            [PipelineStage.Validate] = "验证",
            [PipelineStage.Process] = "处理",
            [PipelineStage.Map] = "映射",
            [PipelineStage.Transform] = "转换",
            [PipelineStage.Save] = "保存",
            [PipelineStage.Cleanup] = "清理",
            [PipelineStage.Complete] = "完成"
        };

        private static readonly Dictionary<PipelineStage, string> StageDescriptions = new()
        {
            [PipelineStage.Initialize] = "初始化管线上下文和资源",
            [PipelineStage.Load] = "加载输入数据和配置",
            [PipelineStage.Validate] = "验证输入数据的有效性",
            [PipelineStage.Process] = "执行业务处理逻辑",
            [PipelineStage.Map] = "数据映射和转换",
            [PipelineStage.Transform] = "数据格式转换",
            [PipelineStage.Save] = "保存处理结果",
            [PipelineStage.Cleanup] = "清理临时资源和状态",
            [PipelineStage.Complete] = "完成管线执行"
        };

        public static IEnumerable<PipelineStage> GetAllStages()
            => Enum.GetValues<PipelineStage>().OrderBy(s => (int)s);

        public static string GetStageName(this PipelineStage stage)
            => StageNames.TryGetValue(stage, out var name) ? name : stage.ToString();

        public static string GetDescription(this PipelineStage stage)
            => StageDescriptions.TryGetValue(stage, out var desc) ? desc : stage.ToString();
    }


    public class Guard
    {
        private int _flag;

        public bool IsSet => _flag != 0;

        public bool Set()
            => Interlocked.CompareExchange(ref _flag, 1, 0) == 0;

        public void Reset()
            => Interlocked.Exchange(ref _flag, 0);

        public bool TryEnter() => Set();

        public void Exit() => Reset();

        public TResult Execute<TResult>(Func<TResult> action, string? guardName = null)
        {
            if (!TryEnter())
                throw new InvalidOperationException(
                    $"Guard '{guardName ?? "unnamed"}' is already entered.");

            try { return action(); }
            finally { Exit(); }
        }

        public async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> asyncAction, string? guardName = null)
        {
            if (!TryEnter())
                throw new InvalidOperationException(
                    $"Guard '{guardName ?? "unnamed"}' is already entered.");

            try { return await asyncAction().ConfigureAwait(false); }
            finally { Exit(); }
        }

        public bool TryExecute(Action action)
        {
            if (!Set()) return false;
            try { action(); return true; }
            finally { Reset(); }
        }

        public async Task<bool> TryExecuteAsync(Func<Task> asyncAction)
        {
            if (!Set()) return false;
            try { await asyncAction().ConfigureAwait(false); return true; }
            finally { Reset(); }
        }
    }


    public class EventSubscriptionManager<TTarget>
        where TTarget : class
    {
        private readonly DedupFlags<TTarget> _subscriptionFlags = new();

        public bool TrySubscribe(TTarget target, Action<TTarget> subscribeAction)
        {
            if (!_subscriptionFlags.TrySet(target))
                return false;

            subscribeAction(target);
            return true;
        }

        public void Unsubscribe(TTarget target)
            => _subscriptionFlags.Reset(target);

        public void Clear()
            => _subscriptionFlags.Clear();
    }


    public enum SessionStatus
    {
        Active,
        Disposed
    }


    public class Session<TTarget, TConfig, TContext> : IDisposable
        where TTarget : class
        where TContext : PipelineContext
    {
        public TConfig Config { get; }

        public TContext Context { get; }

        public IPipeline<TTarget, TTarget> Pipeline { get; }

        public SessionStatus Status { get; private set; } = SessionStatus.Active;

        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        public event Action<Session<TTarget, TConfig, TContext>>? OnDispose;

        public Session(TConfig config, TContext context,
            IPipeline<TTarget, TTarget> pipeline)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public void Dispose()
        {
            if (Status == SessionStatus.Disposed) return;
            Status = SessionStatus.Disposed;
            OnDispose?.Invoke(this);
            Context.Dispose();
            GC.SuppressFinalize(this);
        }
    }


    public class SessionManager<TTarget, TConfig, TContext>
        where TTarget : class
        where TContext : PipelineContext
    {
        private readonly MemoryCache<TTarget, Session<TTarget, TConfig, TContext>> _sessions = new();

        public Session<TTarget, TConfig, TContext> GetOrCreate(TTarget target,
            TConfig config, TContext context,
            IPipeline<TTarget, TTarget> pipeline)
        {
            if (_sessions.TryGet(target, out var existing) && existing.Status == SessionStatus.Active)
                return existing;

            var session = new Session<TTarget, TConfig, TContext>(config, context, pipeline);
            session.OnDispose += s => _sessions.Remove(target);
            return _sessions.GetOrAdd(target, session);
        }

        public Session<TTarget, TConfig, TContext>? GetActiveSession(TTarget target)
        {
            if (_sessions.TryGet(target, out var session) && session.Status == SessionStatus.Active)
                return session;
            return null;
        }

        public void DisposeSession(TTarget target)
        {
            if (_sessions.TryGet(target, out var session))
            {
                session.Dispose();
                _sessions.Remove(target);
            }
        }

        public void DisposeAll()
        {
            var keys = _sessions.Keys.ToList();
            foreach (var key in keys)
                DisposeSession(key);
        }
    }

 
}


