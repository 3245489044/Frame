using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Modules.Parsing.Abstractions;
using Modules.Parsing.Models;
using MetaCommandModel = Modules.Parsing.Models.MetaCommand;

namespace MetaCommand
{
    #region 权限接口

    public interface IPermissionService
    {
        Task<OperationResult<bool>> CheckPermissionAsync(MetaCommandModel command);
        Task<OperationResult<bool>> CheckAdapterAccessAsync(string adapterId);
        Task<OperationResult<IEnumerable<string>>> GetAllowedVerbsAsync();
        Task<OperationResult<IEnumerable<string>>> GetAllowedActionsAsync(string adapterId);
    }

    public interface IPermissionStore
    {
        Task<bool> HasPermissionAsync(string verb, string? action, string? target);
        Task<bool> IsAdapterAllowedAsync(string adapterId);
        Task<IEnumerable<string>> GetAllowedVerbsAsync();
        Task<IEnumerable<string>> GetAllowedActionsAsync(string adapterId);
    }

    #endregion

    #region 权限服务实现

    public class PermissionService : IPermissionService
    {
        private readonly IPermissionStore _permissionStore;

        public PermissionService(IPermissionStore permissionStore)
        {
            _permissionStore = permissionStore ?? throw new ArgumentNullException(nameof(permissionStore));
        }

        public async Task<OperationResult<bool>> CheckPermissionAsync(MetaCommandModel command)
        {
            var hasPermission = await _permissionStore.HasPermissionAsync(
                command.Verb.ToString(),
                command.Action,
                command.Target);

            if (!hasPermission)
            {
                return OperationResult<bool>.FailureResult(
                    $"Permission denied for command: {command.Verb} {command.Action} {command.Target}");
            }

            return OperationResult<bool>.SuccessResult(true, "Permission granted");
        }

        public async Task<OperationResult<bool>> CheckAdapterAccessAsync(string adapterId)
        {
            var isAllowed = await _permissionStore.IsAdapterAllowedAsync(adapterId);

            if (!isAllowed)
            {
                return OperationResult<bool>.FailureResult($"Adapter access denied: {adapterId}");
            }

            return OperationResult<bool>.SuccessResult(true, "Adapter access granted");
        }

        public async Task<OperationResult<IEnumerable<string>>> GetAllowedVerbsAsync()
        {
            var verbs = await _permissionStore.GetAllowedVerbsAsync();
            return OperationResult<IEnumerable<string>>.SuccessResult(verbs, "Allowed verbs retrieved");
        }

        public async Task<OperationResult<IEnumerable<string>>> GetAllowedActionsAsync(string adapterId)
        {
            var actions = await _permissionStore.GetAllowedActionsAsync(adapterId);
            return OperationResult<IEnumerable<string>>.SuccessResult(actions, "Allowed actions retrieved");
        }
    }

    #endregion

    #region 默认权限存储实现

    public class DefaultPermissionStore : IPermissionStore
    {
        private readonly HashSet<string> _allowedVerbs = new()
        {
            nameof(MetaVerb.Do),
            nameof(MetaVerb.Get),
            nameof(MetaVerb.Look),
            nameof(MetaVerb.Wait),
            nameof(MetaVerb.Assert)
        };

        private readonly HashSet<string> _allowedAdapters = new();

        public DefaultPermissionStore(bool allowAllAdapters = true)
        {
            if (allowAllAdapters)
            {
                _allowedAdapters.Add("*");
            }
        }

        public Task<bool> HasPermissionAsync(string verb, string? action, string? target)
        {
            return Task.FromResult(_allowedVerbs.Contains(verb));
        }

        public Task<bool> IsAdapterAllowedAsync(string adapterId)
        {
            return Task.FromResult(_allowedAdapters.Contains("*") || _allowedAdapters.Contains(adapterId));
        }

        public Task<IEnumerable<string>> GetAllowedVerbsAsync()
        {
            return Task.FromResult(_allowedVerbs.AsEnumerable());
        }

        public Task<IEnumerable<string>> GetAllowedActionsAsync(string adapterId)
        {
            return Task.FromResult(Enumerable.Empty<string>());
        }

        public void AllowAdapter(string adapterId) => _allowedAdapters.Add(adapterId);
        public void DenyAdapter(string adapterId) => _allowedAdapters.Remove(adapterId);
    }

    #endregion
}
