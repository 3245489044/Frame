
using System;
using System.Threading.Tasks;

namespace WorkFlowMethod.Guards
{
    public class CallChainGuard
    {
        private static readonly AsyncLocal<int> _callDepth = new();
        private const int MaxCallDepth = 10;

        public async Task<T> ProtectAsync<T>(string callPoint, Func<Task<T>> action)
        {
            _callDepth.Value++;
            if (_callDepth.Value > MaxCallDepth)
            {
                _callDepth.Value--;
                WorkFlowMethod.Diagnostics.WorkFlowMethodLog.Log("CallChainGuard_DepthExceeded", new
                {
                    callPoint,
                    maxDepth = MaxCallDepth,
                    suggestion = "检测到可能的自递归调用（如 Execute→ExecuteCommand→Execute）"
                });
                throw new CallChainExceededException(callPoint, MaxCallDepth);
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
            if (_callDepth.Value > MaxCallDepth)
            {
                _callDepth.Value--;
                throw new CallChainExceededException(callPoint, MaxCallDepth);
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

        public static int GetMaxCallDepth() => MaxCallDepth;
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