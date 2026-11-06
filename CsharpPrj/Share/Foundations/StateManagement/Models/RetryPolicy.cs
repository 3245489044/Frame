namespace Share.StateManagement
{
    /// <summary>
    /// 通用重试策略（唯一实现）：既有「参数 + 延时计算」模型，也提供带条件/回调的 Execute/ExecuteAsync 执行器。
    /// 消费方不得再复制平行实现（FunctionStorage 旧 Core.RetryPolicy 已回收）。
    /// </summary>
    public class RetryPolicy
    {
        public int MaxRetryCount { get; set; } = 3;

        public int RetryDelayMs { get; set; } = 1000;

        public double RetryDelayMultiplier { get; set; } = 2.0;

        /// <summary>是否启用指数退避；关闭时固定 <see cref="RetryDelayMs"/>（映射到乘数 1.0）。</summary>
        public bool UseExponentialBackoff
        {
            get => RetryDelayMultiplier > 1.0;
            set => RetryDelayMultiplier = value ? 2.0 : 1.0;
        }

        /// <summary>兼容别名：最大重试次数。</summary>
        public int MaxRetries
        {
            get => MaxRetryCount;
            set => MaxRetryCount = value;
        }

        /// <summary>兼容别名：基础延时（毫秒）。</summary>
        public int BaseDelayMs
        {
            get => RetryDelayMs;
            set => RetryDelayMs = value;
        }

        /// <summary>仅对满足条件的异常重试；null = 全部重试。</summary>
        public Func<Exception, bool>? RetryCondition { get; set; }

        /// <summary>每次重试前回调（attempt 从 1 开始）。</summary>
        public Action<int, Exception>? OnRetry { get; set; }

        public static RetryPolicy Default => new()
        {
            MaxRetryCount = 3,
            RetryDelayMs = 1000,
            RetryDelayMultiplier = 2.0,
            RetryCondition = ex => ex is TimeoutException || ex is IOException
        };

        public static RetryPolicy NoRetry => new() { MaxRetryCount = 0 };

        public int GetDelayMs(int retryCount)
        {
            // E9：钳制指数溢出到 int.MaxValue，且保证非负（配置异常时不产生负延时）。
            double ms = RetryDelayMs * Math.Pow(RetryDelayMultiplier, Math.Max(0, retryCount));
            if (ms > int.MaxValue) return int.MaxValue;
            return ms < 0 ? 0 : (int)ms;
        }

        /// <summary>执行带重试的异步操作；第 n 次重试延时为 Base × Multiplier^(n-1)。</summary>
        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(operation);
            var attempt = 0;
            while (true)
            {
                try
                {
                    return await operation(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < MaxRetryCount && (RetryCondition?.Invoke(ex) ?? true))
                {
                    attempt++;
                    OnRetry?.Invoke(attempt, ex);
                    await Task.Delay(GetRetryDelayMs(attempt), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>执行带重试的同步操作；第 n 次重试延时为 Base × Multiplier^(n-1)。</summary>
        public T Execute<T>(Func<T> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            var attempt = 0;
            while (true)
            {
                try
                {
                    return operation();
                }
                catch (Exception ex) when (attempt < MaxRetryCount && (RetryCondition?.Invoke(ex) ?? true))
                {
                    attempt++;
                    OnRetry?.Invoke(attempt, ex);
                    Thread.Sleep(GetRetryDelayMs(attempt));
                }
            }
        }

        private int GetRetryDelayMs(int attempt)
        {
            double ms = RetryDelayMs * Math.Pow(RetryDelayMultiplier, Math.Max(0, attempt - 1));
            if (ms > int.MaxValue) return int.MaxValue;
            return ms < 0 ? 0 : (int)ms;
        }
    }
}
