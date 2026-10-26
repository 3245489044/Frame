
using System;
using System.Threading.Tasks;
using Modules.Threading;

namespace WorkFlowMethod.Guards
{
    /// <summary>
    /// 调用链闸门（WFM 门面）：实现已下沉到 <see cref="Modules.Threading.CallDepthGuard"/>（C1）。
    /// 保留原公开 API 与异常类型，避免调用点/测试断裂。
    /// </summary>
    public class CallChainGuard
    {
        /// <summary>声明缺省调用深度上限（实例可经构造参数覆盖；禁止散落字面量）。</summary>
        public const int DefaultMaxCallDepth = 10;

        private readonly CallDepthGuard _guard;

        /// <param name="maxCallDepth">调用深度上限；缺省 = <see cref="DefaultMaxCallDepth"/>（来自声明）。</param>
        public CallChainGuard(int? maxCallDepth = null)
        {
            MaxCallDepth = maxCallDepth ?? DefaultMaxCallDepth;
            _guard = new CallDepthGuard(
                MaxCallDepth,
                static (callPoint, maxDepth) => new CallChainExceededException(callPoint, maxDepth),
                static (callPoint, maxDepth) => WorkFlowMethod.Diagnostics.WorkFlowMethodLog.Log(
                    "CallChainGuard_DepthExceeded",
                    new
                    {
                        callPoint,
                        maxDepth,
                        suggestion = "检测到可能的自递归调用（如 Execute→ExecuteCommand→Execute）"
                    }));
        }

        /// <summary>本实例生效的深度上限。</summary>
        public int MaxCallDepth { get; }

        public Task<T> ProtectAsync<T>(string callPoint, Func<Task<T>> action)
            => _guard.ProtectAsync(callPoint, action);

        public T Protect<T>(string callPoint, Func<T> action)
            => _guard.Protect(callPoint, action);

        public int CurrentDepth => _guard.CurrentDepth;

        public static int GetMaxCallDepth() => DefaultMaxCallDepth;
    }

    public class CallChainExceededException : InvalidOperationException
    {
        public string CallPoint { get; }

        public int MaxDepth { get; }

        public CallChainExceededException(string callPoint, int maxDepth)
            : base($"调用链超出最大深度 {maxDepth}，触发点: '{callPoint}'。检测到可能的自递归调用。")
        {
            CallPoint = callPoint;
            MaxDepth = maxDepth;
        }
    }
}