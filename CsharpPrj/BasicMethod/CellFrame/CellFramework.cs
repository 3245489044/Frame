#nullable enable

using System;
using System.Collections.Generic;
using CellReferenceManagement;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CellReferenceManagement;
#nullable enable

using System;
using System.Collections.Generic;
#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CellReferenceManagement;

namespace CellGateway
{
    public interface IOperationInterceptor
    {
        Task<OperationResult> BeforeExecuteAsync(CellOperation operation);
        Task<OperationResult> AfterExecuteAsync(CellOperation operation, OperationResult result);
    }

    public interface ICellGateway
    {
        OperationResult ExecuteOperation(CellOperation operation);
        List<OperationResult> BatchExecute(List<CellOperation> operations);
        Task<OperationResult> ExecuteOperationAsync(CellOperation operation);
        Task<List<OperationResult>> BatchExecuteAsync(List<CellOperation> operations);
        void RegisterInterceptor(IOperationInterceptor interceptor);
        void EnableAuditLogging(bool enabled);
        List<OperationRecord> GetOperationHistory(string moduleId);
        ValidationResult ValidateAccess(string moduleId, string operation);
        HealthStatus CheckModuleHealth(string moduleId);
        void RegisterModule(string moduleId, ICellModule module);
        void UnregisterModule(string moduleId);
    }

    public class OperationRecord
    {
        public Guid OperationId { get; set; }
        public string ModuleId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool Success { get; set; }
    }

    public enum HealthStatus
    {
        Healthy,
        Degraded,
        Unhealthy
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
namespace CellGateway
{
    public class CellOperation
    {
        public Guid OperationId { get; set; } = Guid.NewGuid();
        public string ModuleId { get; set; } = string.Empty;
        public ModuleType ModuleType { get; set; }
        public string Action { get; set; } = string.Empty;
        public Dictionary<string, object>? Parameters { get; set; } = new Dictionary<string, object>();
        public Guid TransactionId { get; set; } = Guid.Empty;
        public bool RequireAudit { get; set; } = true;
    }

    public class OperationResult
    {
        public Guid OperationId { get; set; }
        public bool Success { get; set; }
        public object? Result { get; set; }
        public Exception? Error { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public enum ModuleType
    {
        Sql,
        Code,
        Config
    }

    public interface ICellModule
    {
        void Initialize(Dictionary<string, object> config);
        void Start();
        void Stop();
        object Execute(string action, Dictionary<string, object> parameters);
        object Query(string query);
        bool Update(Dictionary<string, object> data);
        Dictionary<string, object> GetState();
        void SetState(Dictionary<string, object> state);
    }
}
namespace CellGateway
{
    public class CellGateway : ICellGateway
    {
        private readonly Dictionary<string, ICellModule> _modules = new Dictionary<string, ICellModule>();
        private readonly IReferenceManagementLayer? _referenceManager;
        private readonly ILogger<CellGateway>? _logger;
        private readonly List<IOperationInterceptor> _interceptors = new();
        private readonly List<OperationRecord> _operationHistory = new();
        private bool _auditEnabled = true;

        public CellGateway(IReferenceManagementLayer? referenceManager = null, ILogger<CellGateway>? logger = null)
        {
            _referenceManager = referenceManager;
            _logger = logger;
        }

        public OperationResult ExecuteOperation(CellOperation operation)
        {
            return ExecuteOperationAsync(operation).GetAwaiter().GetResult();
        }

        public List<OperationResult> BatchExecute(List<CellOperation> operations)
        {
            return BatchExecuteAsync(operations).GetAwaiter().GetResult();
        }

        public async Task<OperationResult> ExecuteOperationAsync(CellOperation operation)
        {
            _logger?.LogInformation($"开始执行操作: {operation.ModuleId}.{operation.Action}");

            foreach (var interceptor in _interceptors)
            {
                var beforeResult = await interceptor.BeforeExecuteAsync(operation);
                if (!beforeResult.Success)
                {
                    _logger?.LogWarning($"拦截器阻止操作: {operation.ModuleId}.{operation.Action}");
                    return beforeResult;
                }
            }

            var result = new OperationResult
            {
                OperationId = operation.OperationId,
                Timestamp = DateTime.Now
            };

            try
            {
                if (!_modules.TryGetValue(operation.ModuleId, out var module))
                {
                    result.Success = false;
                    result.Error = new InvalidOperationException($"Module {operation.ModuleId} not found");
                    return result;
                }

                object executeResult = null;
                switch (operation.Action.ToLower())
                {
                    case "execute":
                        executeResult = module.Execute(operation.Parameters["action"].ToString(), operation.Parameters);
                        break;
                    case "query":
                        executeResult = module.Query(operation.Parameters["query"].ToString());
                        break;
                    case "update":
                        executeResult = module.Update(operation.Parameters);
                        break;
                    default:
                        executeResult = module.Execute(operation.Action, operation.Parameters);
                        break;
                }

                result.Success = true;
                result.Result = executeResult;

                if (_referenceManager != null)
                {
                    await _referenceManager.RecordChangeAsync(
                        operation.ModuleId,
                        ConvertToOperationType(operation.Action),
                        null,
                        operation.Parameters ?? new Dictionary<string, object>(),
                        $"Operation: {operation.Action}"
                    );
                }

                foreach (var interceptor in _interceptors)
                {
                    await interceptor.AfterExecuteAsync(operation, result);
                }

                RecordOperationHistory(operation, true);
                _logger?.LogInformation($"操作执行成功: {operation.ModuleId}.{operation.Action}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex;
                RecordOperationHistory(operation, false);
                _logger?.LogError(ex, $"操作执行失败: {operation.ModuleId}.{operation.Action}");
            }

            return result;
        }

        public async Task<List<OperationResult>> BatchExecuteAsync(List<CellOperation> operations)
        {
            var results = new List<OperationResult>();
            var transactionId = Guid.NewGuid();

            foreach (var operation in operations)
            {
                operation.TransactionId = transactionId;
                var result = await ExecuteOperationAsync(operation);
                results.Add(result);
            }

            return results;
        }

        public void RegisterInterceptor(IOperationInterceptor interceptor)
        {
            _interceptors.Add(interceptor);
            _logger?.LogInformation($"注册拦截器: {interceptor.GetType().Name}");
        }

        public void EnableAuditLogging(bool enabled)
        {
            _auditEnabled = enabled;
            _logger?.LogInformation($"审计日志状态: {(_auditEnabled ? "启用" : "禁用")}");
        }

        public List<OperationRecord> GetOperationHistory(string moduleId)
        {
            return _operationHistory
                .Where(r => r.ModuleId == moduleId)
                .OrderByDescending(r => r.Timestamp)
                .ToList();
        }

        public ValidationResult ValidateAccess(string moduleId, string operation)
        {
            return new ValidationResult
            {
                IsValid = true,
                Message = "Access granted"
            };
        }

        public HealthStatus CheckModuleHealth(string moduleId)
        {
            return _modules.ContainsKey(moduleId) ? HealthStatus.Healthy : HealthStatus.Unhealthy;
        }

        public void RegisterModule(string moduleId, ICellModule module)
        {
            _modules[moduleId] = module;
        }

        public void UnregisterModule(string moduleId)
        {
            _modules.Remove(moduleId);
        }

        private void RecordOperationHistory(CellOperation operation, bool success)
        {
            if (_auditEnabled)
            {
                _operationHistory.Add(new OperationRecord
                {
                    OperationId = operation.OperationId,
                    ModuleId = operation.ModuleId,
                    Action = operation.Action,
                    Timestamp = DateTime.UtcNow,
                    Success = success
                });
            }
        }

        private OperationType ConvertToOperationType(string action)
        {
            return action.ToLower() switch
            {
                "create" => OperationType.Create,
                "update" => OperationType.Update,
                "delete" => OperationType.Delete,
                _ => OperationType.Execute
            };
        }
    }

    public class AuditInterceptor : IOperationInterceptor
    {
        private readonly ILogger<AuditInterceptor>? _logger;

        public AuditInterceptor(ILogger<AuditInterceptor>? logger = null)
        {
            _logger = logger;
        }

        public Task<OperationResult> BeforeExecuteAsync(CellOperation operation)
        {
            _logger?.LogInformation($"审计日志 - 准备执行操作: {operation.ModuleId}.{operation.Action}");
            return Task.FromResult(new OperationResult { OperationId = operation.OperationId, Success = true });
        }

        public Task<OperationResult> AfterExecuteAsync(CellOperation operation, OperationResult result)
        {
            _logger?.LogInformation($"审计日志 - 操作完成: {operation.ModuleId}.{operation.Action}, 成功: {result.Success}");
            return Task.FromResult(result);
        }
    }

    public class LogInterceptor : IOperationInterceptor
    {
        private readonly ILogger<LogInterceptor>? _logger;

        public LogInterceptor(ILogger<LogInterceptor>? logger = null)
        {
            _logger = logger;
        }

        public Task<OperationResult> BeforeExecuteAsync(CellOperation operation)
        {
            _logger?.LogDebug($"执行前 - 操作ID: {operation.OperationId}, 模块: {operation.ModuleId}, 动作: {operation.Action}");
            return Task.FromResult(new OperationResult { OperationId = operation.OperationId, Success = true });
        }

        public Task<OperationResult> AfterExecuteAsync(CellOperation operation, OperationResult result)
        {
            _logger?.LogDebug($"执行后 - 操作ID: {operation.OperationId}, 成功: {result.Success}");
            return Task.FromResult(result);
        }
    }
}
namespace CellGateway
{
    public static class CellFramework
    {
        private static ICellGateway? _defaultGateway;
        private static bool _isInitialized = false;

        public static ICellGateway InitializeGateway(IReferenceManagementLayer? referenceManager = null, ILogger? logger = null)
        {
            var gateway = new CellGateway(referenceManager, logger as ILogger<CellGateway>);
            
            gateway.RegisterInterceptor(new AuditInterceptor(logger as ILogger<AuditInterceptor>));
            gateway.RegisterInterceptor(new LogInterceptor(logger as ILogger<LogInterceptor>));
            
            _defaultGateway = gateway;
            _isInitialized = true;
            
            return gateway;
        }

        public static ICellGateway GetDefaultGateway()
        {
            if (!_isInitialized || _defaultGateway == null)
            {
                throw new InvalidOperationException("CellFramework尚未初始化，请先调用InitializeGateway");
            }
            
            return _defaultGateway;
        }

        public static bool IsInitialized => _isInitialized;

        public static void Configure(Action<CellFrameworkOptions> configure)
        {
            var options = new CellFrameworkOptions();
            configure(options);
            
            if (_defaultGateway != null)
            {
                _defaultGateway.EnableAuditLogging(options.EnableAuditLogging);
            }
        }

        public static void Reset()
        {
            _defaultGateway = null;
            _isInitialized = false;
        }
    }

    public class CellFrameworkOptions
    {
        public bool EnableAuditLogging { get; set; } = true;
        public bool EnableReferenceTracking { get; set; } = true;
        public TimeSpan ImprovementCalculationInterval { get; set; } = TimeSpan.FromHours(1);
    }
}