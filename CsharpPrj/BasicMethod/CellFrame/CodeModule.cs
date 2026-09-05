using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorkFlowMethod.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkFlowMethod.Contracts;
using CellGateway;

namespace CellCodeModule
{
    public interface ICodeModule : ICellModule
    {
        void RegisterFunctions(IWorkflowFunctions functions);
        IWorkflowFunctions GetRegisteredFunctions();
        object CallFunction(string functionName, params object[] args);
        Task<object> CallFunctionAsync(string functionName, params object[] args);
        IEnumerable<string> GetAvailableFunctions();
        bool HasFunction(string functionName);
        bool IsAsyncFunction(string functionName);
    }

    public class CodeModuleConfig
    {
        public string FunctionPrefix { get; set; } = "Workflow.";
        public bool EnableAsyncSupport { get; set; } = true;
    }
}
namespace CellCodeModule
{
    public class CodeModule : ICodeModule, IDisposable
    {
        private CodeModuleConfig _config;
        private IWorkflowFunctions _workflowFunctions;
        private readonly Dictionary<string, Func<object[], object>> _syncFunctions = new();
        private readonly Dictionary<string, Func<object[], Task<object>>> _asyncFunctions = new();

        public void Initialize(Dictionary<string, object> config)
        {
            _config = new CodeModuleConfig
            {
                FunctionPrefix = config.TryGetValue("FunctionPrefix", out var fp) ? fp.ToString() : "Workflow.",
                EnableAsyncSupport = config.TryGetValue("EnableAsyncSupport", out var eas) ? Convert.ToBoolean(eas) : true
            };
        }

        public void RegisterFunctions(IWorkflowFunctions functions)
        {
            _workflowFunctions = functions ?? throw new ArgumentNullException(nameof(functions));
            InitializeFunctionMappings();
        }

        private void InitializeFunctionMappings()
        {
            _syncFunctions.Clear();
            _asyncFunctions.Clear();

            if (_workflowFunctions == null)
                return;

            _syncFunctions["Plus"] = args => _workflowFunctions.Plus(
                args.Length > 0 ? args[0] : null, args.Length > 1 ? args[1] : null);
            
            _syncFunctions["Int"] = args => _workflowFunctions.Int(args.Length > 0 ? args[0] : null);
            _syncFunctions["Float"] = args => _workflowFunctions.Float(args.Length > 0 ? args[0] : null);
            _syncFunctions["Double"] = args => _workflowFunctions.Double(args.Length > 0 ? args[0] : null);
            _syncFunctions["String"] = args => _workflowFunctions.String(args.Length > 0 ? args[0] : null);

            _syncFunctions["MergePrefix"] = args => _workflowFunctions.MergePrefix(
                args.Length > 0 ? args[0] : null, args.Skip(1).ToArray());
            _syncFunctions["Concat"] = args => _workflowFunctions.Concat(args);

            _syncFunctions["GetPathValue"] = args => _workflowFunctions.GetPathValue(
                args.Length > 0 ? args[0] as Dictionary<string, object> : null,
                args.Length > 1 ? args[1] : null);
            
            _syncFunctions["SetPathValue"] = args => _workflowFunctions.SetPathValue(
                args.Length > 0 ? args[0] as Dictionary<string, object> : null,
                args.Length > 1 ? args[1] : null,
                args.Length > 2 ? args[2] : null);

            _syncFunctions["ExecuteWorkflow"] = args => _workflowFunctions.ExecuteWorkflow(
                args.Length > 0 ? args[0] as Dictionary<string, object> : null);
            
            if (_config.EnableAsyncSupport)
            {
                _asyncFunctions["ExecuteWorkflowAsync"] = async args => await _workflowFunctions.ExecuteWorkflowAsync(
                    args.Length > 0 ? args[0] as Dictionary<string, object> : null);
            }
        }

        public IWorkflowFunctions GetRegisteredFunctions()
        {
            return _workflowFunctions;
        }

        public void Start()
        {
            if (_workflowFunctions == null)
            {
                _workflowFunctions = new WorkFlowMethod.External.WorkflowFunctions();
                InitializeFunctionMappings();
            }
        }

        public void Stop()
        {
            _syncFunctions.Clear();
            _asyncFunctions.Clear();
        }

        public object CallFunction(string functionName, params object[] args)
        {
            if (_syncFunctions.TryGetValue(functionName, out var func))
            {
                return func(args);
            }

            if (_asyncFunctions.ContainsKey(functionName))
            {
                throw new InvalidOperationException($"异步方法 '{functionName}' 应使用 CallFunctionAsync 调用");
            }

            throw new InvalidOperationException($"未知函数: {functionName}");
        }

        public async Task<object> CallFunctionAsync(string functionName, params object[] args)
        {
            if (_asyncFunctions.TryGetValue(functionName, out var func))
            {
                return await func(args);
            }

            if (_syncFunctions.TryGetValue(functionName, out var syncFunc))
            {
                return syncFunc(args);
            }

            throw new InvalidOperationException($"未知函数: {functionName}");
        }

        public IEnumerable<string> GetAvailableFunctions()
        {
            return _syncFunctions.Keys.Concat(_asyncFunctions.Keys);
        }

        public bool HasFunction(string functionName)
        {
            return _syncFunctions.ContainsKey(functionName) || _asyncFunctions.ContainsKey(functionName);
        }

        public bool IsAsyncFunction(string functionName)
        {
            return _asyncFunctions.ContainsKey(functionName);
        }

        public object Execute(string action, Dictionary<string, object> parameters)
        {
            switch (action.ToLower())
            {
                case "call":
                    var funcName = parameters["functionName"].ToString();
                    var args = parameters.TryGetValue("args", out var argsObj) ? argsObj as object[] : Array.Empty<object>();
                    return CallFunction(funcName, args);
                
                case "callasync":
                    funcName = parameters["functionName"].ToString();
                    args = parameters.TryGetValue("args", out argsObj) ? argsObj as object[] : Array.Empty<object>();
                    return CallFunctionAsync(funcName, args);
                
                case "list":
                    return GetAvailableFunctions().ToList();
                
                case "hasfunction":
                    return HasFunction(parameters["functionName"].ToString());
                
                case "isasync":
                    return IsAsyncFunction(parameters["functionName"].ToString());
                
                default:
                    throw new NotSupportedException($"Action {action} is not supported");
            }
        }

        public object Query(string query)
        {
            switch (query.ToLower())
            {
                case "functions":
                    return GetAvailableFunctions().ToList();
                
                case "registered":
                    return _workflowFunctions != null;
                
                default:
                    return GetAvailableFunctions().ToList();
            }
        }

        public bool Update(Dictionary<string, object> data)
        {
            if (data.TryGetValue("FunctionPrefix", out var fp))
            {
                _config.FunctionPrefix = fp.ToString();
                return true;
            }
            
            if (data.TryGetValue("EnableAsyncSupport", out var eas))
            {
                _config.EnableAsyncSupport = Convert.ToBoolean(eas);
                if (_workflowFunctions != null)
                {
                    InitializeFunctionMappings();
                }
                return true;
            }
            
            return false;
        }

        public Dictionary<string, object> GetState()
        {
            return new Dictionary<string, object>
            {
                { "FunctionCount", _syncFunctions.Count + _asyncFunctions.Count },
                { "SyncFunctionCount", _syncFunctions.Count },
                { "AsyncFunctionCount", _asyncFunctions.Count },
                { "FunctionPrefix", _config.FunctionPrefix },
                { "EnableAsyncSupport", _config.EnableAsyncSupport },
                { "FunctionsRegistered", _workflowFunctions != null }
            };
        }

        public void SetState(Dictionary<string, object> state)
        {
            if (state.TryGetValue("FunctionPrefix", out var fp))
            {
                _config.FunctionPrefix = fp.ToString();
            }
            if (state.TryGetValue("EnableAsyncSupport", out var eas))
            {
                _config.EnableAsyncSupport = Convert.ToBoolean(eas);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}