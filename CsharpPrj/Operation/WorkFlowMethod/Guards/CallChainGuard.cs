
using System;
using System.Threading.Tasks;

namespace WorkFlowMethod.Guards
{
    public class CallChainGuard
    {
        /// <summary>声明缺省调用深度上限（实例可经构造参数覆盖；禁止散落字面量）。</summary>
        public const int DefaultMaxCallDepth = 10;

        private static readonly AsyncLocal<int> _callDepth = new();
        private readonly int _maxCallDepth;

        /// <param name="maxCallDepth">调用深度上限；缺省 = <see cref="DefaultMaxCallDepth"/>（来自声明）。</param>
        public CallChainGuard(int? maxCallDepth = null)
        {
            _maxCallDepth = maxCallDepth ?? DefaultMaxCallDepth;
        }

        /// <summary>本实例生效的深度上限。</summary>
        public int MaxCallDepth => _maxCallDepth;

        public async Task<T> ProtectAsync<T>(string callPoint, Func<Task<T>> action)
        {
            _callDepth.Value++;
            if (_callDepth.Value > _maxCallDepth)
            {
                _callDepth.Value--;
                WorkFlowMethod.Diagnostics.WorkFlowMethodLog.Log("CallChainGuard_DepthExceeded", new
                {
                    callPoint,
                    maxDepth = _maxCallDepth,
                    suggestion = "检测到可能的自递归调用（如 Execute→ExecuteCommand→Execute）"
                });
                throw new CallChainExceededException(callPoint, _maxCallDepth);
            }

            try
            {
                return await action();
            }
            finally
            {
                _callDepth.Value--;
            }
        }

        public T Protect<T>(string callPoint, Func<T> action)
        {
            _callDepth.Value++;
            if (_callDepth.Value > _maxCallDepth)
            {
                _callDepth.Value--;
                throw new CallChainExceededException(callPoint, _maxCallDepth);
            }

            try
            {
                return action();
            }
            finally
            {
                _callDepth.Value--;
            }
        }

        public int CurrentDepth => _callDepth.Value;

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