#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using CellGateway;
using CellReferenceManagement;
using CellSqlStorageModule;
using CellCodeModule;
using CellConfigModule;
#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CellGateway;
using CellReferenceManagement;
using CellSqlStorageModule;
using CellCodeModule;
using CellConfigModule;
using CellChangeTracker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using CellFrameworkHostRelationType = CellReferenceManagement.RelationType;
using CellFrameworkHostDirection = CellReferenceManagement.Direction;
using CellFrameworkHostOperationType = CellReferenceManagement.OperationType;
using CellFrameworkHostChangeRecord = CellReferenceManagement.ChangeRecord;
using CellFrameworkHostValidationResult = CellGateway.ValidationResult;

namespace CellFramework.Host
{
    public class CellFrameworkUsage
    {
        #region 配置属性

        public static string GraphDatabasePath => CellFrameworkConfiguration.GraphDatabaseDirectory;

        public static string GetGraphDatabaseFullPath()
        {
            return CellFrameworkConfiguration.GraphDatabaseDirectory;
        }

        public static string ModuleProjectRootPath => CellFrameworkConfiguration.ModuleProjectRootPath;

        public static string GetModuleProjectFullPath(string relativePath = "")
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return CellFrameworkConfiguration.ModuleProjectRootPath;
            }
            return Path.Combine(CellFrameworkConfiguration.ModuleProjectRootPath, relativePath);
        }

        #endregion

        #region 私有字段

        private readonly ICellGateway _gateway;
        private readonly IReferenceManagementLayer _referenceManager;
        private readonly ISqlStorageModule _sqlModule;
        private readonly ICodeModule _codeModule;
        private readonly IConfigModule _configModule;
        private readonly ILogger? _logger;

        #endregion

        #region 构造函数与工厂方法

        public CellFrameworkUsage(
            ICellGateway gateway,
            IReferenceManagementLayer referenceManager,
            ISqlStorageModule sqlModule,
            ICodeModule codeModule,
            IConfigModule configModule,
            ILogger? logger = null)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _referenceManager = referenceManager ?? throw new ArgumentNullException(nameof(referenceManager));
            _sqlModule = sqlModule ?? throw new ArgumentNullException(nameof(sqlModule));
            _codeModule = codeModule ?? throw new ArgumentNullException(nameof(codeModule));
            _configModule = configModule ?? throw new ArgumentNullException(nameof(configModule));
            _logger = logger;
        }

        public static CellFrameworkUsage CreateFromServiceProvider(IServiceProvider serviceProvider, ILogger? logger = null)
        {
            var gateway = serviceProvider.GetRequiredService<ICellGateway>();
            var referenceManager = serviceProvider.GetRequiredService<IReferenceManagementLayer>();
            var sqlModule = serviceProvider.GetRequiredService<ISqlStorageModule>();
            var codeModule = serviceProvider.GetRequiredService<ICodeModule>();
            var configModule = serviceProvider.GetRequiredService<IConfigModule>();

            return new CellFrameworkUsage(gateway, referenceManager, sqlModule, codeModule, configModule, logger);
        }

        #endregion

        #region SQL存储模块操作

        public Guid BeginTransaction()
        {
            return _sqlModule.BeginTransaction();
        }

        public bool CommitTransaction(Guid transactionId)
        {
            return _sqlModule.CommitTransaction(transactionId);
        }

        public bool RollbackTransaction(Guid transactionId)
        {
            return _sqlModule.RollbackTransaction(transactionId);
        }

        public DataTable ExecuteSqlQuery(string sql, Dictionary<string, object>? parameters = null)
        {
            return _sqlModule.ExecuteQuery(sql, parameters ?? new Dictionary<string, object>());
        }

        public int ExecuteSqlNonQuery(string sql, Dictionary<string, object>? parameters = null)
        {
            return _sqlModule.ExecuteNonQuery(sql, parameters ?? new Dictionary<string, object>());
        }

        public object? ExecuteSqlScalar(string sql, Dictionary<string, object>? parameters = null)
        {
            return _sqlModule.ExecuteScalar(sql, parameters ?? new Dictionary<string, object>());
        }

        public ConnectionState GetSqlConnectionState()
        {
            return _sqlModule.GetConnectionState();
        }

        public bool UpdateSqlData(Dictionary<string, object> data)
        {
            return _sqlModule.Update(data);
        }

        #endregion

        #region 代码模块操作

        public object CallCodeFunction(string functionName, params object[] args)
        {
            return _codeModule.CallFunction(functionName, args);
        }

        public async Task<object> CallCodeFunctionAsync(string functionName, params object[] args)
        {
            return await _codeModule.CallFunctionAsync(functionName, args);
        }

        public bool IsCodeFunctionAvailable(string functionName)
        {
            return _codeModule.HasFunction(functionName);
        }

        public IEnumerable<string> GetAvailableCodeFunctions()
        {
            return _codeModule.GetAvailableFunctions();
        }

        public void StartCodeModule()
        {
            _codeModule.Start();
        }

        public void StopCodeModule()
        {
            _codeModule.Stop();
        }

        #endregion

        #region 配置模块操作

        public T? GetConfigValue<T>(string key)
        {
            return _configModule.GetConfig<T>(key);
        }

        public object? GetConfigValue(string key)
        {
            return _configModule.GetConfig(key);
        }

        public bool SetConfigValue(string key, object value)
        {
            return _configModule.SetConfig(key, value);
        }

        public List<string> GetAllConfigKeys()
        {
            return _configModule.GetAllKeys();
        }

        public bool ClearConfigValue(string key)
        {
            return _configModule.ClearConfig(key);
        }

        public bool LoadConfigFromFile(string filePath)
        {
            return _configModule.LoadConfig(filePath);
        }

        public bool SaveConfigToFile(string filePath)
        {
            return _configModule.SaveConfig(filePath);
        }

        public void StartConfigModule()
        {
            _configModule.Start();
        }

        public void StopConfigModule()
        {
            _configModule.Stop();
        }

        public void ReloadConfig()
        {
            _configModule.ReloadConfig();
        }

        public bool ValidateConfig(object schema)
        {
            return _configModule.ValidateConfig(schema).IsValid;
        }

        #endregion

        #region 引用管理操作

        public Guid CreateReference(string source, string target, CellFrameworkHostRelationType type)
        {
            return _referenceManager.CreateReference(source, target, type);
        }

        public async Task<Guid> CreateReferenceAsync(string source, string target, CellFrameworkHostRelationType type)
        {
            return await _referenceManager.CreateReferenceAsync(source, target, type);
        }

        public bool RemoveReference(string source, string target, CellFrameworkHostRelationType type)
        {
            return _referenceManager.RemoveReference(source, target, type);
        }

        public List<ReferenceRelation> GetReferences(string resourceId, CellFrameworkHostDirection direction)
        {
            return _referenceManager.GetReferences(resourceId, direction);
        }

        public List<ReferenceRelation> GetDependencies(string resourceId, int depth = 5)
        {
            return _referenceManager.GetDependencies(resourceId, depth);
        }

        public async Task<List<ReferenceRelation>> GetDependenciesAsync(string resourceId, int depth = 5)
        {
            return await _referenceManager.GetDependenciesAsync(resourceId, depth);
        }

        #endregion

        #region 变更记录操作

        public Guid RecordChange(string moduleId, CellFrameworkHostOperationType operation, Dictionary<string, object>? before, Dictionary<string, object>? after, string? reason = null)
        {
            return _referenceManager.RecordChange(moduleId, operation, before, after, reason);
        }

        public async Task<Guid> RecordChangeAsync(string moduleId, CellFrameworkHostOperationType operation, Dictionary<string, object>? before, Dictionary<string, object>? after, string? reason = null)
        {
            return await _referenceManager.RecordChangeAsync(moduleId, operation, before, after, reason);
        }

        public CellFrameworkHostChangeRecord? GetChangeRecord(Guid recordId)
        {
            return _referenceManager.GetChangeRecord(recordId);
        }

        public async Task<CellFrameworkHostChangeRecord?> GetChangeRecordAsync(Guid recordId)
        {
            return await _referenceManager.GetChangeRecordAsync(recordId);
        }

        public List<CellFrameworkHostChangeRecord> QueryChanges(ChangeFilter? filter = null)
        {
            return _referenceManager.QueryChanges(filter ?? new ChangeFilter());
        }

        public async Task<List<CellFrameworkHostChangeRecord>> QueryChangesAsync(ChangeFilter? filter = null)
        {
            return await _referenceManager.QueryChangesAsync(filter ?? new ChangeFilter());
        }

        public async Task<Stream> ExportChangesAsync(DateTime from, DateTime to)
        {
            return await _referenceManager.ExportChangesAsync(from, to);
        }

        #endregion

        #region 依赖图分析

        public DependencyGraph AnalyzeDependencyGraph()
        {
            return _referenceManager.AnalyzeDependencyGraph();
        }

        public async Task<DependencyGraph> AnalyzeDependencyGraphAsync()
        {
            return await _referenceManager.AnalyzeDependencyGraphAsync();
        }

        public List<CircularDependency> DetectCircularDependencies()
        {
            return _referenceManager.DetectCircularDependencies();
        }

        public double CalculateImprovement(string moduleId)
        {
            return _referenceManager.CalculateImprovement(moduleId);
        }

        public async Task<double> CalculateImprovementAsync(string moduleId)
        {
            return await _referenceManager.CalculateImprovementAsync(moduleId);
        }

        #endregion

        #region 集合操作

        public ChangeVector<T> CalculateSetDifference<T>(IEnumerable<T> before, IEnumerable<T> after)
        {
            var calculator = new DiscreteChangeCalculator<T>();
            return calculator.CalculateSetDifference(before, after);
        }

        public List<T> CalculateSetIntersection<T>(IEnumerable<T> set1, IEnumerable<T> set2)
        {
            var calculator = new DiscreteChangeCalculator<T>();
            return calculator.CalculateSetIntersection(set1, set2);
        }

        public List<T> CalculateSetUnion<T>(IEnumerable<T> set1, IEnumerable<T> set2)
        {
            var calculator = new DiscreteChangeCalculator<T>();
            return calculator.CalculateSetUnion(set1, set2);
        }

        public StateSnapshot<T> CreateSnapshot<T>(IEnumerable<T> data)
        {
            var calculator = new DiscreteChangeCalculator<T>();
            return calculator.CreateSnapshot(data);
        }

        public ChangeReport<T> GenerateChangeReport<T>(IEnumerable<T> before, IEnumerable<T> after, Func<T, T, bool>? isModified = null)
        {
            var calculator = new DiscreteChangeCalculator<T>();
            return calculator.GenerateChangeReport(before, after, isModified);
        }

        #endregion

        #region 网关操作

        public OperationResult ExecuteGatewayOperation(string moduleId, string action, Dictionary<string, object>? parameters = null)
        {
            var operation = new CellOperation
            {
                ModuleId = moduleId,
                Action = action,
                Parameters = parameters ?? new Dictionary<string, object>()
            };
            return _gateway.ExecuteOperation(operation);
        }

        public async Task<OperationResult> ExecuteGatewayOperationAsync(string moduleId, string action, Dictionary<string, object>? parameters = null)
        {
            var operation = new CellOperation
            {
                ModuleId = moduleId,
                Action = action,
                Parameters = parameters ?? new Dictionary<string, object>()
            };
            return await _gateway.ExecuteOperationAsync(operation);
        }

        public List<OperationResult> BatchExecuteGatewayOperations(List<CellOperation> operations)
        {
            return _gateway.BatchExecute(operations);
        }

        public CellFrameworkHostValidationResult ValidateModuleAccess(string moduleId, string operation)
        {
            return _gateway.ValidateAccess(moduleId, operation);
        }

        public HealthStatus CheckModuleHealth(string moduleId)
        {
            return _gateway.CheckModuleHealth(moduleId);
        }

        public Dictionary<string, HealthStatus> CheckAllModulesHealth()
        {
            var modules = new[] { "sql", "code", "config" };
            var result = new Dictionary<string, HealthStatus>();
            foreach (var module in modules)
            {
                result[module] = _gateway.CheckModuleHealth(module);
            }
            return result;
        }

        public List<OperationRecord> GetModuleOperationHistory(string moduleId)
        {
            return _gateway.GetOperationHistory(moduleId);
        }

        public void RegisterGatewayInterceptor(IOperationInterceptor interceptor)
        {
            _gateway.RegisterInterceptor(interceptor);
        }

        public void EnableGatewayAuditLogging(bool enabled)
        {
            _gateway.EnableAuditLogging(enabled);
        }

        #endregion

        #region 模块状态操作

        public Dictionary<string, object> GetModuleState(string moduleId)
        {
            var operation = new CellOperation
            {
                ModuleId = moduleId,
                Action = "GetState"
            };
            var result = _gateway.ExecuteOperation(operation);
            return result.Result as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        public bool SetModuleState(string moduleId, Dictionary<string, object> state)
        {
            var operation = new CellOperation
            {
                ModuleId = moduleId,
                Action = "SetState",
                Parameters = new Dictionary<string, object> { { "state", state } }
            };
            var result = _gateway.ExecuteOperation(operation);
            return result.Success;
        }

        #endregion
    }
}
namespace CellFramework.Host
{
    using CellFrameworkStatic = CellGateway.CellFramework;
    
    public class CellFrameworkHost
    {
        private IServiceProvider? _serviceProvider;
        private ILogger<CellFrameworkHost>? _logger;
        private IConfiguration? _configuration;
        private CancellationTokenSource? _cts;

        public async Task RunAsync(string[] args)
        {
            try
            {
                _cts = new CancellationTokenSource();
                
                ConfigureServices(args);
                
                _logger = _serviceProvider?.GetRequiredService<ILogger<CellFrameworkHost>>();
                _logger?.LogInformation("========================================");
                _logger?.LogInformation("CELL Framework Host 启动中...");
                _logger?.LogInformation("========================================");

                await InitializeFrameworkAsync();

                _logger?.LogInformation("CELL Framework Host 已成功启动");
                _logger?.LogInformation("按 Ctrl+C 停止服务...");

                RegisterShutdownHandler();

                await WaitForShutdownAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogCritical(ex, "CELL Framework Host 启动失败");
                throw;
            }
        }

        private void ConfigureServices(string[] args)
        {
            var services = new ServiceCollection();

            _configuration = CellFrameworkConfiguration.CreateConfigurationBuilder(args)
                .Build();

            services.AddSingleton(_configuration);

            services.AddLogging(configure =>
            {
                configure.AddConsole();
                configure.AddDebug();
                configure.SetMinimumLevel(LogLevel.Information);
            });

            services.AddSingleton<IReferenceManagementLayer, ReferenceManagementLayer>();
            services.AddSingleton<ICellGateway, CellGateway.CellGateway>();

            services.AddTransient<ISqlStorageModule, SqlStorageModule>();
            services.AddTransient<ICodeModule, CodeModule>();
            services.AddTransient<IConfigModule, ConfigModule>();

            services.AddSingleton<CellFrameworkHost>(this);

            _serviceProvider = services.BuildServiceProvider();
        }

        private async Task InitializeFrameworkAsync()
        {
            if (_serviceProvider == null)
                throw new InvalidOperationException("服务提供程序未初始化");

            var referenceManager = _serviceProvider.GetRequiredService<IReferenceManagementLayer>();
            var logger = _serviceProvider.GetRequiredService<ILogger<CellGateway.CellGateway>>();
            
            var gateway = CellFrameworkStatic.InitializeGateway(referenceManager, logger);
            _logger?.LogInformation("框架网关初始化完成");

            var sqlModule = _serviceProvider.GetRequiredService<ISqlStorageModule>();
            sqlModule.Initialize(new Dictionary<string, object>
            {
                { "ConnectionString", _configuration?["CellFramework:Modules:SqlStorage:ConnectionString"] ?? "" },
                { "ProviderName", _configuration?["CellFramework:Modules:SqlStorage:ProviderName"] ?? "SQLite" },
                { "EnableTransaction", true }
            });
            gateway.RegisterModule("sql", sqlModule);
            _logger?.LogInformation("SQL存储模块注册完成");

            var codeModule = _serviceProvider.GetRequiredService<ICodeModule>();
            codeModule.Initialize(new Dictionary<string, object>
            {
                { "FunctionPrefix", _configuration?["CellFramework:Modules:Code:FunctionPrefix"] ?? "Workflow." },
                { "EnableAsyncSupport", true }
            });
            codeModule.Start();
            gateway.RegisterModule("code", codeModule);
            _logger?.LogInformation("代码功能模块注册完成");

            var configModule = _serviceProvider.GetRequiredService<IConfigModule>();
            configModule.Initialize(new Dictionary<string, object>
            {
                { "ConfigPath", _configuration?["CellFramework:Modules:Config:ConfigPath"] ?? "./config/" },
                { "ConfigFileName", _configuration?["CellFramework:Modules:Config:ConfigFileName"] ?? "config.json" },
                { "EnableAutoReload", false }
            });
            configModule.Start();
            gateway.RegisterModule("config", configModule);
            _logger?.LogInformation("配置模块注册完成");

            CellFrameworkStatic.Configure(options =>
            {
                options.EnableAuditLogging = bool.Parse(_configuration?["CellFramework:AuditEnabled"] ?? "true");
                options.EnableReferenceTracking = bool.Parse(_configuration?["CellFramework:ReferenceTracking"] ?? "true");
            });

            await Task.CompletedTask;
        }

        private void RegisterShutdownHandler()
        {
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                _logger?.LogInformation("收到停止信号，正在关闭...");
                eventArgs.Cancel = true;
                _cts?.Cancel();
            };
        }

        private async Task WaitForShutdownAsync()
        {
            if (_cts == null)
                return;

            try
            {
                await Task.Delay(Timeout.Infinite, _cts.Token);
            }
            catch (TaskCanceledException)
            {
            }

            await ShutdownAsync();
        }

        private async Task ShutdownAsync()
        {
            _logger?.LogInformation("正在关闭CELL Framework Host...");

            var gateway = CellFrameworkStatic.GetDefaultGateway();
            
            if (gateway != null)
            {
                var sqlModule = gateway.GetOperationHistory("sql");
                var codeModule = gateway.GetOperationHistory("code");
                var configModule = gateway.GetOperationHistory("config");
                
                _logger?.LogInformation($"SQL模块操作记录: {sqlModule.Count}");
                _logger?.LogInformation($"代码模块操作记录: {codeModule.Count}");
                _logger?.LogInformation($"配置模块操作记录: {configModule.Count}");
            }

            CellFrameworkStatic.Reset();
            _cts?.Dispose();

            _logger?.LogInformation("========================================");
            _logger?.LogInformation("CELL Framework Host 已成功关闭");
            _logger?.LogInformation("========================================");

            await Task.CompletedTask;
        }
    }
}
