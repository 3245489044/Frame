
using System.Runtime.CompilerServices;

namespace Modules.Common.Async
{
    public static class AsyncHelper
    {
        public static async void FireAndForget(
            Func<Task> asyncAction,
            Action<Exception>? onError = null,
            Action? onCancel = null)
        {
            try
            {
                await asyncAction();
            }
            catch (OperationCanceledException)
            {
                onCancel?.Invoke();
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }

        public static async void FireAndForget(
            Func<Task> asyncAction,
            string operationName,
            Action<Exception>? onError = null)
        {
            try
            {
                await asyncAction();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }

        public static async void FireAndForget<TResult>(
            Func<Task<TResult>> asyncFunc,
            Action<TResult>? onResult = null,
            Action<Exception>? onError = null)
        {
            try
            {
                var result = await asyncFunc();
                onResult?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }

        public static async Task<TResult> WithTimeout<TResult>(
            Func<CancellationToken, Task<TResult>> asyncFunc,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, cancellationToken);

            try
            {
                return await asyncFunc(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"操作在 {timeout.TotalSeconds} 秒内未完成，已超时。");
            }
        }

        public static async Task<TResult> WithRetry<TResult>(
            Func<Task<TResult>> asyncFunc,
            int maxRetries = 3,
            TimeSpan? retryDelay = null,
            Action<int, Exception>? onRetry = null,
            CancellationToken cancellationToken = default)
        {
            retryDelay ??= TimeSpan.FromSeconds(1);

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await asyncFunc();
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    onRetry?.Invoke(attempt + 1, ex);
                    await Task.Delay(retryDelay.Value, cancellationToken);
                }
            }

            throw new InvalidOperationException("重试逻辑异常");
        }
    }
}
