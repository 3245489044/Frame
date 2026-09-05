#region USINGS
using System;
using System.Collections.Generic;
using System.Threading;
using WorkFlowMethod.Execution.Session;
using WorkflowExecution.Abstractions;
using ExecutionFramework.Abstractions;
#endregion

namespace WorkFlowMethod.Execution.Adapters
{
public class ShareContextAdapter : TConfig.IExecutionContext
    {
        private readonly ExecutionSession _session;
        private readonly string _executionId;
        private readonly string _workflowId;
        private readonly string _nodeKey;
        private CancellationToken _cancellationToken;

        private IReadOnlyDictionary<string, object>? _parametersCache;

        public ShareContextAdapter(
            ExecutionSession session,
            string? nodeKey = null,
            CancellationToken cancellationToken = default)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _executionId = session.SessionId;
            _workflowId = session.SessionId;
            _nodeKey = nodeKey ?? string.Empty;
            _cancellationToken = cancellationToken;
        }

        public string ExecutionId => _executionId;

        public string WorkflowId => _workflowId;

        public string NodeKey => _nodeKey;

        public IReadOnlyDictionary<string, object> Parameters
        {
            get
            {
                if (_parametersCache == null)
                {
                    var dict = new Dictionary<string, object>();
                    foreach (var key in _session.Data.Keys)
                    {
                        dict[key] = _session.Data.Get(key);
                    }
                    _parametersCache = dict;
                }
                return _parametersCache;
            }
        }

        public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();

        public CancellationToken CancellationToken
        {
            get => _cancellationToken;
            set => _cancellationToken = value;
        }

        public IServiceProvider? ServiceProvider { get; set; }

        public T GetParameter<T>(string key)
        {
            if (_session.Data.TryGet(key, out var value) && value is T typedValue)
                return typedValue;

            throw new KeyNotFoundException($"Parameter '{key}' not found or type mismatch");
        }

        public T GetParameter<T>(int index)
        {
            var keys = new List<string>(_session.Data.Keys);
            if (index >= 0 && index < keys.Count)
            {
                var value = _session.Data.Get(keys[index]);
                if (value is T typedValue)
                    return typedValue;
            }

            throw new IndexOutOfRangeException($"Parameter at index {index} not found or type mismatch");
        }

        public bool TryGetParameter<T>(string key, out T value)
        {
            if (_session.Data.TryGet(key, out var obj) && obj is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = default!;
            return false;
        }

        public T GetData<T>(string key)
        {
            if (_session.Data.TryGet(key, out var value) && value is T typedValue)
                return typedValue;

            throw new KeyNotFoundException($"Data item '{key}' not found or type mismatch");
        }

        public bool TryGetData<T>(string key, out T value)
        {
            if (_session.Data.TryGet(key, out var obj) && obj is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = default!;
            return false;
        }

        public void SetData<T>(string key, T value)
        {
            _session.Data.Set(key, value!);
        }

        public TConfig.IExecutionContext CreateSubContext(string? nodeKey = null)
        {
            return new ShareContextAdapter(
                _session,
                nodeKey ?? _nodeKey,
                _cancellationToken)
            {
                ServiceProvider = ServiceProvider
            };
        }

        public ExecutionSession GetSession() => _session;
    }
}
