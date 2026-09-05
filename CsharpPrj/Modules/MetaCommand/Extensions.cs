using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Parsing.Abstractions;
using Modules.Parsing.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MetaCommand
{
    public static class MetaCommandExtensions
    {
        #region 服务注册

        public static IServiceCollection AddMetaCommand(this IServiceCollection services,
    Action<MetaCommandOptions>? configureOptions = null)
        {
            var options = new MetaCommandOptions();
            configureOptions?.Invoke(options);

            services.AddSingleton(options);
            services.AddSingleton<ICommandStatistics, CommandStatistics>();
            services.AddSingleton<IPermissionStore, DefaultPermissionStore>();
            services.AddSingleton<IPermissionService, PermissionService>();
            services.AddSingleton<MiddlewarePipeline>();
            services.AddSingleton<ICommandRouter, CommandRouter>();

            return services;
        }

        #endregion

        #region 命令执行扩展

        public static async Task<OperationResult<object>> ExecuteCommandAsync(this ICommandRouter router,
                                                                                string command,
                                                                                CancellationToken cancellationToken = default)
        {
            return await router.ExecuteCommandAsync(command, cancellationToken);
        }

        public static async Task<OperationResult<object>> ExecuteDoAsync(this ICommandRouter router,
    string action,
    string? target = null,
    Dictionary<string, object>? parameters = null,
    CancellationToken cancellationToken = default)
        {
            var command = MetaVerb.Do.BuildCommand(target, action, parameters);
            return await router.ExecuteCommandAsync(command, cancellationToken);
        }

        public static async Task<OperationResult<object>> ExecuteGetAsync(this ICommandRouter router,
                                                                            string entity,
                                                                            Dictionary<string, object>? queryParams = null,
                                                                            CancellationToken cancellationToken = default)
        {
            var command = MetaVerb.Get.BuildCommand(entity, parameters: queryParams);
            return await router.ExecuteCommandAsync(command, cancellationToken);
        }

        public static async Task<OperationResult<object>> ExecuteLookAsync(this ICommandRouter router,
    string? scope = null,
    Dictionary<string, object>? options = null,
    CancellationToken cancellationToken = default)
        {
            var command = MetaVerb.Look.BuildCommand(scope, parameters: options);
            return await router.ExecuteCommandAsync(command, cancellationToken);
        }

        #endregion

        #region 执行器扩展

        public static void RegisterAndActivateExecutor(this ICommandRouter router,
                                                        string executorId,
                                                        ICommandExecutor executor)
        {
            router.RegisterExecutor(executorId, executor);
            router.SetActiveExecutor(executorId);
        }

        public static bool IsExecutorRegistered(this ICommandRouter router, string executorId)
        {
            return router.TryGetExecutor(executorId, out _);
        }

        public static int ExecutorCount(this ICommandRouter router)
        {
            return router.GetRegisteredExecutors().Count();
        }

        #endregion


        #region 指标扩展

        public static string GetMetricsSummary(this ICommandRouter router)
        {
            var metrics = router.GetMetrics();
            return $"Total: {metrics.TotalExecutions}, " +
                $"Success: {metrics.SuccessfulExecutions}, " +
                $"Failed: {metrics.FailedExecutions}, " +
                $"Rate: {metrics.SuccessRate:P2}, " +
                $"Avg: {metrics.AverageDurationMs:F2}ms";
        }

        public static bool HasRecentErrors(this ICommandRouter router)
        {
            return router.GetMetrics().RecentErrors.Count > 0;
        }

        #endregion

        #region 命令构建扩展

        public static string BuildCommand(this MetaVerb verb, string? target = null,
                                            string? action = null,
                                            Dictionary<string, object>? parameters = null)
        {
            var parts = new List<string> { verb.ToString().ToLower() };

            if (verb == MetaVerb.Do)
            {
                if (!string.IsNullOrEmpty(action))
                    parts.Add(action);
                if (!string.IsNullOrEmpty(target))
                    parts.Add(target);
            }
            else
            {
                if (!string.IsNullOrEmpty(action))
                {
                    if (parameters == null)
                        parameters = new Dictionary<string, object>();
                    parameters["action"] = action;
                }
                if (!string.IsNullOrEmpty(target))
                    parts.Add(target);
            }

            if (parameters != null)
            {
                parts.AddRange(parameters.Select(p => $"{p.Key}={p.Value}"));
            }

            return string.Join(" ", parts);
        }

        #endregion
    }
}
