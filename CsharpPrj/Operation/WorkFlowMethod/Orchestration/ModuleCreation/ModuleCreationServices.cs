
#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkFlowMethod.Diagnostics;
using WorkFlowMethod.Orchestration.Content;
using WorkFlowMethod.Skeletons.NodeProcess.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.Orchestration.ModuleCreation
{
    public class ModuleCreationConfig
    {
        public string WorkflowId { get; }
        public string Symbol { get; }
        public int ChainCount { get; }
        public string JsonConfig { get; }
        public bool EnableValidation { get; set; } = true;

        public ModuleCreationConfig(string workflowId, string symbol,
            int chainCount = 1, string jsonConfig = null)
        {
            WorkflowId = workflowId ?? throw new ArgumentNullException(nameof(workflowId));
            Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
            ChainCount = chainCount > 0 ? chainCount : throw new ArgumentException("ChainCount 必须大于 0");
            JsonConfig = jsonConfig;
        }
    }

    public class ModuleCreationResult
    {
        public bool Success { get; }
        public string WorkflowId { get; }
        public IReadOnlyList<string> ChainIds { get; }
        public IReadOnlyList<string> NodeKeys { get; }
        public string ErrorMessage { get; }

        private ModuleCreationResult(bool success, string workflowId,
            IReadOnlyList<string> chainIds, IReadOnlyList<string> nodeKeys,
            string errorMessage)
        {
            Success = success;
            WorkflowId = workflowId;
            ChainIds = chainIds ?? Array.Empty<string>();
            NodeKeys = nodeKeys ?? Array.Empty<string>();
            ErrorMessage = errorMessage;
        }

        public static ModuleCreationResult CreateSuccess(string workflowId,
            List<string> chainIds, List<string> nodeKeys) =>
            new(true, workflowId, chainIds.AsReadOnly(), nodeKeys.AsReadOnly(), null);

        public static ModuleCreationResult CreateFailure(string workflowId, string error) =>
            new(false, workflowId, null, null, error);
    }

    public interface IModuleCreator
    {
        Task<ModuleCreationResult> CreateModuleAsync(ModuleCreationConfig config,
            CancellationToken cancellationToken = default);
    }

    public class DefaultModuleCreator : IModuleCreator
    {
        public IWorkflowMediator Mediator { get; set; }

        public async Task<ModuleCreationResult> CreateModuleAsync(
            ModuleCreationConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var mediator = Mediator ?? CreateDefaultMediator();

                var chainConfig = new ChainConfig
                {
                    WorkflowId = config.WorkflowId,
                    Chains = new List<ConnectionPositionInfo<string>>
                    {
                        new ConnectionPositionInfo<string>
                        {
                            ChainConfigValue = config.Symbol,
                            ListCount = config.ChainCount
                        }
                    }
                };

                var chainInfo = mediator.BuildFromConfig(chainConfig);
                var chainIds = new List<string> { chainInfo?.StartNode?.basicConfig?.Callkey ?? config.Symbol };
                var nodeKeys = chainInfo?.AllNodes?.Select(n => n.basicConfig?.Callkey).Where(k => k != null).ToList() ?? new List<string>();

                return ModuleCreationResult.CreateSuccess(
                    config.WorkflowId,
                    chainIds,
                    nodeKeys);
            }
            catch (Exception ex)
            {
                WorkFlowMethodLog.Log("DefaultModuleCreator_CreateModuleAsync_failed", new { workflowId = config.WorkflowId, symbol = config.Symbol, error = ex.Message });
                return ModuleCreationResult.CreateFailure(config.WorkflowId, ex.Message);
            }
        }

        private static IWorkflowMediator CreateDefaultMediator()
        {
            return new WorkflowMediator();
        }
    }

    public class ModuleCreationManager
    {
        private readonly IModuleCreator _creator;
        private readonly List<ModuleCreationConfig> _pendingCreations = new();
        private bool _initialized;

        public ModuleCreationManager(IModuleCreator creator = null)
        {
            _creator = creator ?? new DefaultModuleCreator();
        }

        public void RegisterPendingCreation(ModuleCreationConfig config)
        {
            if (_initialized)
                throw new InvalidOperationException("模块管理器已初始化，无法注册新的待创建模块");

            _pendingCreations.Add(config ?? throw new ArgumentNullException(nameof(config)));
        }

        public async Task<List<ModuleCreationResult>> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            if (_initialized)
                throw new InvalidOperationException("模块管理器已初始化");

            _initialized = true;
            var results = new List<ModuleCreationResult>();

            foreach (var config in _pendingCreations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await _creator.CreateModuleAsync(config, cancellationToken);
                results.Add(result);
            }

            _pendingCreations.Clear();
            return results;
        }

        public bool IsInitialized => _initialized;
    }

    public static class ModuleCreationExtensions
    {
        public static async Task<ModuleCreationResult> CreateModuleSafeAsync(
            this IModuleCreator creator,
            ModuleCreationConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await creator.CreateModuleAsync(config, cancellationToken);
            }
            catch (Exception ex)
            {
                WorkFlowMethodLog.Log("ModuleCreationExtensions_CreateModuleSafeAsync_error", new { error = ex.Message });
                return ModuleCreationResult.CreateFailure(config.WorkflowId, "创建模块时发生异常");
            }
        }
    }
}