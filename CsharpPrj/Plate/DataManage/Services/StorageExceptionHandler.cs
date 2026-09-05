#nullable enable

using System;
using System.Threading.Tasks;
using DataManage.Exceptions;

namespace DataManage.Services
{
    public interface IStorageExceptionHandler
    {
        Task<T> ExecuteWithExceptionHandlingAsync<T>(Func<Task<T>> operation, string operationName, string containerName = "");
        Task ExecuteWithExceptionHandlingAsync(Func<Task> operation, string operationName, string containerName = "");
        void LogException(Exception ex, string operationName, string containerName = "");
    }

    public class StorageExceptionHandler : IStorageExceptionHandler
    {
        private readonly IStorageLogger _logger;

        public StorageExceptionHandler(IStorageLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<T> ExecuteWithExceptionHandlingAsync<T>(Func<Task<T>> operation, string operationName, string containerName = "")
        {
            try
            {
                _logger.LogDebug($"开始执行存储操作: {operationName}, 容器: {containerName}");
                var result = await operation();
                _logger.LogDebug($"存储操作成功: {operationName}, 容器: {containerName}");
                return result;
            }
            catch (DuplicateKeyException ex)
            {
                _logger.LogWarning($"唯一键冲突: {ex.Message}");
                throw;
            }
            catch (DataNotFoundException ex)
            {
                _logger.LogWarning($"数据未找到: {ex.Message}");
                throw;
            }
            catch (StorageInitializationException ex)
            {
                _logger.LogError($"存储初始化失败: {ex.Message}", ex);
                throw;
            }
            catch (StorageException ex)
            {
                _logger.LogError($"存储操作失败: {ex.Message}", ex);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"存储操作发生未预期异常: {ex.Message}", ex);
                throw new StorageException($"存储操作失败: {operationName}", null, ex);
            }
        }

        public async Task ExecuteWithExceptionHandlingAsync(Func<Task> operation, string operationName, string containerName = "")
        {
            try
            {
                _logger.LogDebug($"开始执行存储操作: {operationName}, 容器: {containerName}");
                await operation();
                _logger.LogDebug($"存储操作成功: {operationName}, 容器: {containerName}");
            }
            catch (DuplicateKeyException ex)
            {
                _logger.LogWarning($"唯一键冲突: {ex.Message}");
                throw;
            }
            catch (DataNotFoundException ex)
            {
                _logger.LogWarning($"数据未找到: {ex.Message}");
                throw;
            }
            catch (StorageInitializationException ex)
            {
                _logger.LogError($"存储初始化失败: {ex.Message}", ex);
                throw;
            }
            catch (StorageException ex)
            {
                _logger.LogError($"存储操作失败: {ex.Message}", ex);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"存储操作发生未预期异常: {ex.Message}", ex);
                throw new StorageException($"存储操作失败: {operationName}", null, ex);
            }
        }

        public void LogException(Exception ex, string operationName, string containerName = "")
        {
            _logger.LogError($"存储异常: {operationName}, 容器: {containerName}, 错误: {ex.Message}", ex);
        }
    }

    public interface IStorageLogger
    {
        void LogDebug(string message);
        void LogInformation(string message);
        void LogWarning(string message);
        void LogError(string message, Exception? exception = null);
    }

    public class ConsoleStorageLogger : IStorageLogger
    {
        public void LogDebug(string message)
        {
            Console.WriteLine($"[DEBUG] [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        }

        public void LogInformation(string message)
        {
            Console.WriteLine($"[INFO] [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        }

        public void LogWarning(string message)
        {
            Console.WriteLine($"[WARN] [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        }

        public void LogError(string message, Exception? exception = null)
        {
            Console.WriteLine($"[ERROR] [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
            if (exception != null)
            {
                Console.WriteLine($"[ERROR] [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 异常详情: {exception}");
            }
        }
    }

    public static class StorageExceptionHandlerExtensions
    {
        public static async Task<T> WithExceptionHandlingAsync<T>(this Task<T> task, IStorageExceptionHandler handler, string operationName, string containerName = "")
        {
            return await handler.ExecuteWithExceptionHandlingAsync(() => task, operationName, containerName);
        }

        public static async Task WithExceptionHandlingAsync(this Task task, IStorageExceptionHandler handler, string operationName, string containerName = "")
        {
            await handler.ExecuteWithExceptionHandlingAsync(() => task, operationName, containerName);
        }
    }
}