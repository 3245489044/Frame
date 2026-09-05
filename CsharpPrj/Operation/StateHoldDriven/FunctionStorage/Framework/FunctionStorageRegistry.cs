
using System.Collections.Concurrent;
using Common.Diagnostics;
using TConfig;

using FunctionStorage.Abstractions;
using ExecutionFramework.Engine;
using WorkFlowMethod.Usage;
using IFunctionDescriptor = TConfig.IFunctionDescriptor;
using IFunctionUnit = FunctionStorage.Abstractions.IFunctionUnit;
using ExecutionResult = TConfig.ExecutionResult;

namespace FunctionStorage
{
    public static class FunctionStorageRegistry
    {
        private static ConcurrentDictionary<string, IFunctionUnit>? _functionUnits;
        private static readonly object _unitLock = new();

        public static ITrConfigRegistry Registry
        {
            get
            {
                var reg = Share.Registry.RegistryCoordinator.Registry;
                if (reg is ITrConfigRegistry funcReg)
                    return funcReg;
                throw new InvalidCastException(
                    $"RegistryCoordinator.Registry 的类型为 {reg.GetType().FullName}，" +
                    $"未实现 ITrConfigRegistry 接口。");
            }
        }

        public static void Initialize(ITrConfigRegistry registry)
        {
            Share.Registry.RegistryCoordinator.Initialize(registry);
        }


        public static void Register(string name, Delegate function, string? description = null)
        {
            Registry.Register(name, function, description);
            FsLog.Log("FunctionStorageRegistry_Register_delegate", new {
                Name = name,
                Description = description ?? ""
            });
        }

        public static void Register(IFunctionUnit unit)
        {
            ArgumentNullException.ThrowIfNull(unit);
            if (string.IsNullOrWhiteSpace(unit.Name))
                throw new ArgumentException("Function unit name cannot be empty", nameof(unit));

            var registry = Registry;

            lock (_unitLock)
            {
                _functionUnits ??= new ConcurrentDictionary<string, IFunctionUnit>(StringComparer.OrdinalIgnoreCase);
                _functionUnits[unit.Name] = unit;

                registry.Register(unit.Name, (Func<object?, Task<ExecutionResult>>)(async (input) =>
                {
                    return await unit.ExecuteAsync(input);
                }), unit.Description);
            }

            FsLog.Log("FunctionStorageRegistry_Register_unit", new {
                Name = unit.Name,
                TotalCount = Registry.GetRegisteredNames().Count()
            });
        }


        public static bool TryGetUnit(string name, out IFunctionUnit unit)
        {
            lock (_unitLock)
            {
                if (_functionUnits == null)
                {
                    unit = null!;
                    return false;
                }
                return _functionUnits.TryGetValue(name, out unit!);
            }
        }

        public static bool Contains(string name) => Registry.Contains(name);

        public static IEnumerable<string> GetRegisteredNames() => Registry.GetRegisteredNames();

        public static bool TryGetDescriptor(string name, out IFunctionDescriptor descriptor)
        {
            return Registry.TryGetDescriptor(name, out descriptor);
        }

        public static void Unregister(string name)
        {
            Registry.Unregister(name);
            lock (_unitLock)
            {
                _functionUnits?.TryRemove(name, out _);
            }
        }

        public static void Clear()
        {
            Registry.Clear();
            lock (_unitLock)
            {
                _functionUnits?.Clear();
            }
            NonEventCommandCenter.Registry.Clear();
        }


        public static async Task<ExecutionResult[]> ExecuteBatchAsync(params string[] functionNames)
        {
            return await ExecuteBatchAsync(functionNames, CancellationToken.None);
        }

        public static async Task<ExecutionResult[]> ExecuteBatchAsync(string[] functionNames, CancellationToken cancellationToken)
        {
            const int DefaultTimeoutMs = 30_000;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(DefaultTimeoutMs);

            var executor = WorkFlowMethodUsage.Executor;
            // 批量执行无参调用：原实现把函数名自身当参数传入（复制粘贴错误），改为空参数
            var tasks = functionNames.Select(name => executor.ExecuteAsync(name, Array.Empty<object>())).ToArray();
            var allTasks = Task.WhenAll(tasks);

            try
            {
                var completedTask = await Task.WhenAny(allTasks, Task.Delay(-1, cts.Token));
                if (completedTask != allTasks)
                {
                    return functionNames.Select(name => ExecutionResult.Failure(
                        $"批量执行超时（{DefaultTimeoutMs}ms）")).ToArray();
                }

                return await allTasks;
            }
            catch (OperationCanceledException)
            {
                return functionNames.Select(name => ExecutionResult.Failure(
                    "批量执行已取消")).ToArray();
            }
        }
    }


    public class FunctionStorageLifecycle : Modules.Registry.IRegistryLifecycle
    {
        private bool _initialized;

        public Modules.Registry.FunctionRegistry Registry
            => (Modules.Registry.FunctionRegistry)Share.Registry.RegistryCoordinator.Registry;

        public ExecutionFramework.Engine.FunctionExecutor Executor { get; }

        public FunctionStorageLifecycle()
        {
            var ctorReg = Share.Registry.RegistryCoordinator.Registry;
            var ctorHash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ctorReg).ToString("X8");
            var ctorType = ctorReg.GetType().FullName;
            var ctorCount = ctorReg.GetRegisteredNames().Count();
            FsLog.Log("FunctionStorageLifecycle_ctor_registry_diag", new
            {
                hash = ctorHash,
                type = ctorType,
                count = ctorCount,
                isInitialized = Share.Registry.RegistryCoordinator.IsInitialized,
                message = "FunctionStorageLifecycle 构造函数中获取的注册表实例"
            });

            Executor = new ExecutionFramework.Engine.FunctionExecutor(
                ctorReg,
                new ExecutionFramework.Abstractions.NullParameterProcessor(),
                new ExecutionFramework.Engine.FallbackTypeConverter());
        }

        public bool IsInitialized => _initialized;

        public void Initialize()
        {
            if (_initialized) return;


            _initialized = true;

            FsLog.Log("FunctionStorageLifecycle_Initialize_registeredCount", new
            {
                count = ((ITrConfigRegistry)Share.Registry.RegistryCoordinator.Registry).GetRegisteredNames().Count()
            });
        }

        public void Shutdown()
        {
            if (!_initialized) return;
            FunctionStorageRegistry.Clear();
            _initialized = false;
        }
    }
}