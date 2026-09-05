
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Diagnostics;
using Modules.DiagnosticContext;
using Modules.Parsing.Models;
using ICommandExecutor = Modules.Parsing.Abstractions.ICommandExecutor;

namespace MetaCommand
{
    public class LogQueryAdapter : ICommandExecutor
    {
        private readonly LogQueryFS _queryFS;

        public string ExecutorId { get; }
        public string Name { get; }
        public string Version { get; }

        public LogQueryAdapter()
        {
            ExecutorId = "logfs";
            Name = "Log FileSystem Adapter";
            Version = "1.0";
            _queryFS = new LogQueryFS();
        }

        public Task<OperationResult<object>> DoAsync(string action, string? target,
                                                       Dictionary<string, object>? parameters,
                                                       CancellationToken cancellationToken = default)
        {
            switch (action.ToLowerInvariant())
            {
                case "query":
                case "cat":
                case "find":
                    return Task.FromResult(ExecuteQuery(parameters));

                case "ls":
                case "list":
                    return Task.FromResult(ExecuteList(parameters));

                default:
                    return Task.FromResult(
                        OperationResult<object>.FailureResult(
                            $"Unknown action: {action}. Supported: query, ls, cat, find",
                            ErrorCode.ActionNotFound));
            }
        }

        public Task<OperationResult<object>> GetAsync(string entity,
                                                        Dictionary<string, object>? queryParams,
                                                        CancellationToken cancellationToken = default)
        {
            if (entity.Equals("stat", StringComparison.OrdinalIgnoreCase) ||
                entity.Equals("stats", StringComparison.OrdinalIgnoreCase))
            {
                var path = queryParams?.GetValueOrDefault("path")?.ToString() ?? string.Empty;
                var stats = LogQueryFS.GetStats(path);
                return Task.FromResult(
                    OperationResult<object>.SuccessResult(
                        stats,
                        stats.ToFormattedText()));
            }

            return Task.FromResult(
                OperationResult<object>.FailureResult(
                    $"Unknown entity: {entity}. Supported: stat",
                    ErrorCode.TargetNotFound));
        }

        public Task<OperationResult<WorldModel>> LookAsync(string? scope,
                                                             Dictionary<string, object>? options,
                                                             CancellationToken cancellationToken = default)
        {
            var path = scope ?? "/logs";

            var worldModel = new WorldModel
            {
                AdapterId = ExecutorId,
                Scope = path,
                Timestamp = DateTime.UtcNow,
                State = new Dictionary<string, object>
                {
                    ["type"] = "logfs",
                    ["description"] = "Log FileSystem - query logs by category path"
                }
            };

            var directories = LogQueryFS.ListDirectories(path);
            worldModel.Entities = directories.Select(d => new EntityDescriptor
            {
                Name = d.TrimEnd('/'),
                Type = "log-category",
                Description = $"Log category: {d}",
                SupportedActions = new List<string> { "query", "ls", "stat" }
            }).ToList();

            worldModel.Metadata["totalCategories"] = LogCategoryRegistry.Count.ToString();
            worldModel.Metadata["tree"] = LogCategoryRegistry.GetTreeJson();

            return Task.FromResult(
                OperationResult<WorldModel>.SuccessResult(
                    worldModel,
                    $"Log categories under {path}: {string.Join(", ", directories)}"));
        }

        public Task<OperationResult<bool>> WaitAsync(string condition, int timeoutMs,
                                                       CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                OperationResult<bool>.FailureResult(
                    "Wait is not supported by LogQueryAdapter",
                    ErrorCode.ActionNotFound));
        }

        public Task<OperationResult<bool>> AssertAsync(string predicate,
                                                         Dictionary<string, object>? parameters,
                                                         CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                OperationResult<bool>.FailureResult(
                    "Assert is not supported by LogQueryAdapter",
                    ErrorCode.ActionNotFound));
        }

        public Task<OperationResult<List<EntityDescriptor>>> DiscoverAsync(string? scope,
                                                                             CancellationToken cancellationToken = default)
        {
            var path = scope ?? "/logs";
            var directories = LogQueryFS.ListDirectories(path);

            var entities = directories.Select(d => new EntityDescriptor
            {
                Name = d.TrimEnd('/'),
                Type = "log-category",
                Description = $"Log category: {d}",
                SupportedActions = new List<string> { "query", "ls", "stat" }
            }).ToList();

            return Task.FromResult(
                OperationResult<List<EntityDescriptor>>.SuccessResult(
                    entities,
                    $"Discovered {entities.Count} log categories under {path}"));
        }


        private OperationResult<object> ExecuteQuery(Dictionary<string, object>? parameters)
        {
            var filter = new LogQueryFilter();

            if (parameters != null)
            {
                if (parameters.TryGetValue("path", out var path))
                    filter.Path = path?.ToString();

                if (parameters.TryGetValue("level", out var level))
                    filter.Level = level?.ToString();

                if (parameters.TryGetValue("since", out var since))
                    filter.Since = since?.ToString();

                if (parameters.TryGetValue("keyword", out var keyword))
                    filter.Keyword = keyword?.ToString();

                if (parameters.TryGetValue("limit", out var limit) && limit is int limitVal)
                    filter.Limit = limitVal;

                if (parameters.TryGetValue("recursive", out var recursive))
                    filter.Recursive = recursive is bool b ? b : true;
            }

            var result = LogQueryFS.Query(filter);

            return OperationResult<object>.SuccessResult(
                result,
                result.ToFormattedText());
        }

        private OperationResult<object> ExecuteList(Dictionary<string, object>? parameters)
        {
            var path = parameters?.GetValueOrDefault("path")?.ToString() ?? "/logs";
            var directories = LogQueryFS.ListDirectories(path);

            var result = new
            {
                path,
                directories = directories.ToList(),
                count = directories.Count()
            };

            return OperationResult<object>.SuccessResult(
                result,
                $"Directory listing for {path}:\n  " + string.Join("\n  ", directories));
        }
    }
}
