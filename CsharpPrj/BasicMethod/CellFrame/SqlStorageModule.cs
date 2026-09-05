using System;
using System.Collections.Generic;
using System.Data;
using CellReferenceManagement;
using System;
using System.Collections.Generic;
using System.Data;
using CellGateway;

namespace CellSqlStorageModule
{
    public interface ISqlStorageModule : ICellModule
    {
        DataTable ExecuteQuery(string sql, Dictionary<string, object> parameters);
        int ExecuteNonQuery(string sql, Dictionary<string, object> parameters);
        object ExecuteScalar(string sql, Dictionary<string, object> parameters);
        Guid BeginTransaction();
        bool CommitTransaction(Guid transactionId);
        bool RollbackTransaction(Guid transactionId);
        ConnectionState GetConnectionState();
    }

    public class SqlStorageConfig
    {
        public string ConnectionString { get; set; }
        public string ProviderName { get; set; } = "SQLite";
        public int MaxPoolSize { get; set; } = 10;
        public int CommandTimeout { get; set; } = 30;
        public bool EnableTransaction { get; set; } = true;
    }
}
namespace CellSqlStorageModule
{
    public class SqlStorageModule : ISqlStorageModule
    {
        private readonly IReferenceManagementLayer? _referenceManager;
        private SqlStorageConfig _config;
        private ConnectionState _connectionState = ConnectionState.Closed;

        public SqlStorageModule(IReferenceManagementLayer? referenceManager = null)
        {
            _referenceManager = referenceManager;
        }

        public void Initialize(Dictionary<string, object> config)
        {
            _config = new SqlStorageConfig
            {
                ConnectionString = config.TryGetValue("ConnectionString", out var cs) ? cs.ToString() : "",
                ProviderName = config.TryGetValue("ProviderName", out var pn) ? pn.ToString() : "SQLite",
                MaxPoolSize = config.TryGetValue("MaxPoolSize", out var mps) ? Convert.ToInt32(mps) : 10,
                CommandTimeout = config.TryGetValue("CommandTimeout", out var ct) ? Convert.ToInt32(ct) : 30,
                EnableTransaction = config.TryGetValue("EnableTransaction", out var et) ? Convert.ToBoolean(et) : true
            };
        }

        public void Start()
        {
            _connectionState = ConnectionState.Connecting;
            
            _connectionState = ConnectionState.Open;
        }

        public void Stop()
        {
            _connectionState = ConnectionState.Closed;
        }

        public DataTable ExecuteQuery(string sql, Dictionary<string, object> parameters)
        {
            var result = new DataTable();
            result.Columns.Add("Id", typeof(string));
            result.Columns.Add("Data", typeof(object));
            
            if (_referenceManager != null)
            {
                var references = _referenceManager.GetReferences(sql, Direction.Both);
                foreach (var reference in references)
                {
                    result.Rows.Add(reference.Id.ToString(), reference);
                }
            }
            
            return result;
        }

        public int ExecuteNonQuery(string sql, Dictionary<string, object> parameters)
        {
            if (_referenceManager != null && parameters != null)
            {
                var cellId = parameters.TryGetValue("CellId", out var id) ? id.ToString() : Guid.NewGuid().ToString();
                
                _referenceManager.RecordChange(
                    cellId,
                    OperationType.Create,
                    null,
                    parameters,
                    $"SQL ExecuteNonQuery: {sql}"
                );
            }
            
            return 1;
        }

        public object ExecuteScalar(string sql, Dictionary<string, object> parameters)
        {
            if (_referenceManager != null && parameters != null)
            {
                var cellId = parameters.TryGetValue("CellId", out var id) ? id.ToString() : string.Empty;
                var references = _referenceManager.GetReferences(cellId, Direction.Both);
                return references?.Count > 0 ? references[0] : null;
            }
            
            return null;
        }

        public Guid BeginTransaction()
        {
            if (!_config.EnableTransaction)
                throw new NotSupportedException("Transaction is not enabled");

            return Guid.NewGuid();
        }

        public bool CommitTransaction(Guid transactionId)
        {
            return true;
        }

        public bool RollbackTransaction(Guid transactionId)
        {
            return true;
        }

        public ConnectionState GetConnectionState()
        {
            return _connectionState;
        }

        public object Execute(string action, Dictionary<string, object> parameters)
        {
            return action.ToLower() switch
            {
                "query" => ExecuteQuery(parameters["sql"].ToString(), parameters),
                "nonquery" => ExecuteNonQuery(parameters["sql"].ToString(), parameters),
                "scalar" => ExecuteScalar(parameters["sql"].ToString(), parameters),
                "begintransaction" => BeginTransaction(),
                "commit" => CommitTransaction(Guid.Parse(parameters["transactionId"].ToString())),
                "rollback" => RollbackTransaction(Guid.Parse(parameters["transactionId"].ToString())),
                _ => throw new NotSupportedException($"Action {action} is not supported")
            };
        }

        public object Query(string query)
        {
            return ExecuteQuery(query, new Dictionary<string, object>());
        }

        public bool Update(Dictionary<string, object> data)
        {
            if (!data.TryGetValue("CellId", out var cellId))
                return false;

            if (_referenceManager != null)
            {
                _referenceManager.RecordChange(
                    cellId.ToString(),
                    OperationType.Update,
                    null,
                    data,
                    "SQL Update"
                );
            }
            
            return true;
        }

        public Dictionary<string, object> GetState()
        {
            return new Dictionary<string, object>
            {
                { "ConnectionString", _config.ConnectionString },
                { "ProviderName", _config.ProviderName },
                { "MaxPoolSize", _config.MaxPoolSize },
                { "CommandTimeout", _config.CommandTimeout },
                { "EnableTransaction", _config.EnableTransaction },
                { "ConnectionState", _connectionState.ToString() }
            };
        }

        public void SetState(Dictionary<string, object> state)
        {
            if (state.TryGetValue("EnableTransaction", out var et))
            {
                _config.EnableTransaction = Convert.ToBoolean(et);
            }
            if (state.TryGetValue("CommandTimeout", out var ct))
            {
                _config.CommandTimeout = Convert.ToInt32(ct);
            }
        }
    }
}
