
using System.Collections.Concurrent;
using System.Diagnostics;
using CommandFramework.Abstractions;
using ParameterResolver.Abstractions;
using ParameterResolver.Builder;
using TConfig;
using IFunctionUnit = FunctionStorage.Abstractions.IFunctionUnit;


// 注：原 RegistryIFunctionExecutor/RegistryITrConfigRegistry 等别名随 FunctionExecutor 死代码删除而移除

namespace FunctionStorage.Core
{

    public class FunctionExecutionContext : TConfig.IExecutionContext
    {
        public string? CurrentCommand { get; set; }

        public IParsedCommand? ParsedCommand { get; set; }

        public string? BoundFunctionName { get; set; }

        public IReadOnlyDictionary<string, object?>? ExtractedValues { get; set; }

        public object? FunctionResultValue { get; set; }

        public bool FunctionExecuteSuccess { get; set; }

        public string? FunctionErrorMessage { get; set; }

        public IDictionary<string, object?> SharedData { get; } = new ConcurrentDictionary<string, object?>(StringComparer.OrdinalIgnoreCase);


        string TConfig.IExecutionContext.ExecutionId => Guid.NewGuid().ToString("N");

        string TConfig.IExecutionContext.WorkflowId
            => SharedData.TryGetValue("__WorkflowId", out var v) && v is string s ? s : string.Empty;

        string TConfig.IExecutionContext.NodeKey
            => SharedData.TryGetValue("__NodeKey", out var v) && v is string s ? s : string.Empty;

        IReadOnlyDictionary<string, object> TConfig.IExecutionContext.Parameters
        {
            get
            {
                var parameters = new Dictionary<string, object>();
                if (ParsedCommand?.Parameters != null)
                {
                    foreach (var kvp in ParsedCommand.Parameters)
                        parameters[kvp.Key] = kvp.Value;
                }
                return parameters;
            }
        }

        IDictionary<string, object> TConfig.IExecutionContext.Items
        {
            get
            {
                var dict = new Dictionary<string, object>();
                foreach (var kvp in SharedData)
                    dict[kvp.Key] = kvp.Value ?? string.Empty;
                return dict;
            }
        }

        CancellationToken TConfig.IExecutionContext.CancellationToken { get; set; }

        IServiceProvider? TConfig.IExecutionContext.ServiceProvider
        {
            get => SharedData.TryGetValue("__ServiceProvider", out var v) && v is IServiceProvider sp ? sp : null;
            set { if (value != null) SharedData["__ServiceProvider"] = value; }
        }

        T TConfig.IExecutionContext.GetParameter<T>(string key)
        {
            if (ParsedCommand?.Parameters != null &&
                ParsedCommand.Parameters.TryGetValue(key, out var val))
            {
                if (val is T typed) return typed;
                try { return (T)Convert.ChangeType(val, typeof(T)); } catch { }
            }
            throw new KeyNotFoundException($"参数未找到: {key}");
        }

        T TConfig.IExecutionContext.GetParameter<T>(int index)
        {
            if (ParsedCommand?.Parameters != null && index >= 0 && index < ParsedCommand.Parameters.Count)
            {
                var val = ParsedCommand.Parameters.Values.ElementAt(index);
                if (val is T typed) return typed;
                try { return (T)Convert.ChangeType(val, typeof(T)); } catch { }
            }
            throw new KeyNotFoundException($"参数索引未找到: {index}");
        }

        bool TConfig.IExecutionContext.TryGetParameter<T>(string key, out T value)
        {
            if (ParsedCommand?.Parameters != null &&
                ParsedCommand.Parameters.TryGetValue(key, out var val))
            {
                if (val is T typed) { value = typed; return true; }
                try { value = (T)Convert.ChangeType(val, typeof(T)); return true; } catch { }
            }
            value = default!;
            return false;
        }

        T TConfig.IExecutionContext.GetData<T>(string key)
        {
            if (SharedData.TryGetValue(key, out var val))
            {
                if (val is T typed) return typed;
                try { return (T)Convert.ChangeType(val, typeof(T)); } catch { }
            }
            throw new KeyNotFoundException($"数据项未找到: {key}");
        }

        bool TConfig.IExecutionContext.TryGetData<T>(string key, out T value)
        {
            if (SharedData.TryGetValue(key, out var val))
            {
                if (val is T typed) { value = typed; return true; }
                try { value = (T)Convert.ChangeType(val, typeof(T)); return true; } catch { }
            }
            value = default!;
            return false;
        }

        void TConfig.IExecutionContext.SetData<T>(string key, T value)
        {
            SharedData[key] = value;
        }

        TConfig.IExecutionContext TConfig.IExecutionContext.CreateSubContext(string? nodeKey)
        {
            return new FunctionExecutionContext
            {
                CurrentCommand = CurrentCommand,
                ParsedCommand = ParsedCommand,
                BoundFunctionName = BoundFunctionName,
                ExtractedValues = ExtractedValues,
                FunctionResultValue = FunctionResultValue,
                FunctionExecuteSuccess = FunctionExecuteSuccess,
                FunctionErrorMessage = FunctionErrorMessage
            };
        }

        public virtual void Reset()
        {
            CurrentCommand = null;
            ParsedCommand = null;
            BoundFunctionName = null;
            ExtractedValues = null;
            FunctionResultValue = null;
            FunctionExecuteSuccess = false;
            FunctionErrorMessage = null;
            SharedData.Clear();
        }
    }



}
