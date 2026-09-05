
#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.Block
{

    public class DiagnosticBlock : BlockBase
    {
        private readonly Func<BlockContext, bool> _checkFunc;
        private readonly string _checkDescription;

        public DiagnosticBlock(
            string name,
            Func<BlockContext, bool> checkFunc,
            string? checkDescription = null)
            : base(name, $"诊断检查: {checkDescription ?? name}")
        {
            _checkFunc = checkFunc ?? throw new ArgumentNullException(nameof(checkFunc));
            _checkDescription = checkDescription ?? name;
        }

        public override Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var passed = _checkFunc(context);
                return Task.FromResult(passed
                    ? BlockResult.Ok($"诊断检查通过: {_checkDescription}")
                    : BlockResult.Fail($"诊断检查失败: {_checkDescription}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(BlockResult.Fail($"诊断检查异常: {_checkDescription}", ex));
            }
        }
    }


    public class ConditionalBlock : BlockBase
    {
        private readonly Func<BlockContext, bool> _condition;
        private readonly IBlock? _trueBlock;
        private readonly IBlock? _falseBlock;

        public ConditionalBlock(
            string name,
            Func<BlockContext, bool> condition,
            IBlock? trueBlock = null,
            IBlock? falseBlock = null,
            string? description = null)
            : base(name, description ?? $"条件分支: {name}")
        {
            _condition = condition ?? throw new ArgumentNullException(nameof(condition));
            _trueBlock = trueBlock;
            _falseBlock = falseBlock;
        }

        public override async Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
        {
            var conditionResult = _condition(context);

            if (conditionResult)
            {
                if (_trueBlock != null)
                    return await _trueBlock.ExecuteAsync(context, cancellationToken);
                return BlockResult.Ok("条件为 true，无 trueBlock");
            }
            else
            {
                if (_falseBlock != null)
                    return await _falseBlock.ExecuteAsync(context, cancellationToken);
                return BlockResult.Ok("条件为 false，无 falseBlock");
            }
        }
    }


    public class TimeoutBlock : BlockBase
    {
        private readonly IBlock _innerBlock;
        private readonly TimeSpan _timeout;

        public TimeoutBlock(
            string name,
            IBlock innerBlock,
            TimeSpan timeout,
            string? description = null)
            : base(name, description ?? $"超时控制: {name} ({timeout.TotalMilliseconds}ms)")
        {
            _innerBlock = innerBlock ?? throw new ArgumentNullException(nameof(innerBlock));
            _timeout = timeout;
        }

        public override async Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_timeout);

            try
            {
                return await _innerBlock.ExecuteAsync(context, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return BlockResult.Fail($"块执行超时 ({_timeout.TotalMilliseconds}ms)");
            }
        }
    }


    public class LogBlock : BlockBase
    {
        private readonly string _message;
        private readonly Action<string>? _logAction;

        public LogBlock(
            string name,
            string message,
            Action<string>? logAction = null)
            : base(name, $"日志: {message}")
        {
            _message = message;
            _logAction = logAction;
        }

        public override Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
        {
            var logMessage = $"[{Name}] {_message}";
            if (_logAction != null)
                _logAction(logMessage);
            else
                Console.WriteLine(logMessage);

            return Task.FromResult(BlockResult.Ok($"日志已记录: {_message}"));
        }
    }


    public class SequenceBlock : BlockBase
    {
        private readonly ExecutionPipeline _innerPipeline;

        public SequenceBlock(
            string name,
            ExecutionPipeline innerPipeline,
            string? description = null)
            : base(name, description ?? $"子序列: {name}")
        {
            _innerPipeline = innerPipeline ?? throw new ArgumentNullException(nameof(innerPipeline));
        }

        public override async Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
        {
            var result = await _innerPipeline.RunAsync(context, cancellationToken);

            if (result.OverallSuccess)
                return BlockResult.Ok($"子序列执行完成 ({result.SuccessCount}/{result.SuccessCount + result.FailedCount} 块成功)");

            return BlockResult.Fail($"子序列执行失败 ({result.FailedCount} 块失败)");
        }
    }


    public class DelayBlock : BlockBase
    {
        private readonly TimeSpan _delay;

        public DelayBlock(string name, TimeSpan delay)
            : base(name, $"延迟: {delay.TotalMilliseconds}ms")
        {
            _delay = delay;
        }

        public override async Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken);
            return BlockResult.Ok($"延迟 {_delay.TotalMilliseconds}ms 完成");
        }
    }
}
