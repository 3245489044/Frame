#region USING
using LiteDB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

#nullable enable

namespace UniversalWorkflowFramework.Storage
{
    public interface ILiteDBPersistence<TKey, TNodeData, TEdgeData> : IDisposable
        where TKey : notnull
        where TNodeData : class
        where TEdgeData : class
    {
        Task InitializeAsync(string databasePath);
        Task SaveNodeAsync(TKey key, TNodeData data, string? symbol = null);
        Task SaveEdgeAsync(string edgeId, TKey source, TKey target, bool isDirected, TEdgeData? data, double weight = 1.0);
        Task<TNodeData?> LoadNodeAsync(TKey key);
        Task<TEdgeData?> LoadEdgeAsync(string edgeId);
        Task RemoveNodeAsync(TKey key);
        Task RemoveEdgeAsync(string edgeId);
        Task<List<TKey>> GetAllNodeKeysAsync();
        Task<List<string>> GetAllEdgeIdsAsync();
        Task<bool> ContainsNodeAsync(TKey key);
        Task<bool> ContainsEdgeAsync(string edgeId);
        Task ClearAllAsync();
    }

    public class LiteDBPersistence<TKey, TNodeData, TEdgeData> : ILiteDBPersistence<TKey, TNodeData, TEdgeData>, IDisposable
        where TKey : notnull
        where TNodeData : class
        where TEdgeData : class
    {
        private const string NodesCollectionName = "Nodes";
        private const string EdgesCollectionName = "Edges";
        private LiteDatabase? _database;
        private ILiteCollection<NodeDocument<TKey, TNodeData>>? _nodesCollection;
        private ILiteCollection<EdgeDocument<TKey, TEdgeData>>? _edgesCollection;
        private bool _disposed;
        private readonly SemaphoreSlim _dbLock = new SemaphoreSlim(1, 1);

        private async Task<T> ExecuteWithErrorHandlingAsync<T>(Func<T> action, string errorMessage)
        {
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{errorMessage}: {ex.Message}", ex);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        private async Task ExecuteWithErrorHandlingAsync(Action action, string errorMessage)
        {
            await ExecuteWithErrorHandlingAsync<object?>(() => { action(); return null; }, errorMessage);
        }

        public async Task InitializeAsync(string databasePath) => await ExecuteWithErrorHandlingAsync(() =>
        {
            _database = new LiteDatabase($"Filename={databasePath};Connection=shared");
            _nodesCollection = _database.GetCollection<NodeDocument<TKey, TNodeData>>(NodesCollectionName);
            _edgesCollection = _database.GetCollection<EdgeDocument<TKey, TEdgeData>>(EdgesCollectionName);
            _nodesCollection.EnsureIndex(x => x.Key, true);
            _edgesCollection.EnsureIndex(x => x.EdgeId, true);
        }, "Failed to initialize LiteDB persistence");

        public async Task SaveNodeAsync(TKey key, TNodeData data, string? symbol = null) => await ExecuteWithErrorHandlingAsync(() =>
        {
            if (_nodesCollection == null) throw new InvalidOperationException("LiteDB persistence not initialized");

            var existing = _nodesCollection.FindById(new BsonValue(key));
            DateTime createdAt = existing?.CreatedAt ?? DateTime.UtcNow;


            _nodesCollection.Upsert(new NodeDocument<TKey, TNodeData>
            {
                Key = key,
                Data = data,
                Symbol = symbol,
                CreatedAt = createdAt,
                UpdatedAt = DateTime.UtcNow
            });
        }, "Failed to save node");

        public async Task SaveEdgeAsync(string edgeId, TKey source, TKey target, bool isDirected, TEdgeData? data, double weight = 1.0) => await ExecuteWithErrorHandlingAsync(() =>
        {
            if (_edgesCollection == null) throw new InvalidOperationException("LiteDB persistence not initialized");

            var existing = _edgesCollection.FindById(edgeId);
            DateTime createdAt = existing?.CreatedAt ?? DateTime.UtcNow;

            _edgesCollection.Upsert(new EdgeDocument<TKey, TEdgeData>
            {
                EdgeId = edgeId,
                Source = source,
                Target = target,
                IsDirected = isDirected,
                Data = data,
                Weight = weight,
                CreatedAt = createdAt,
                UpdatedAt = DateTime.UtcNow
            });
        }, "Failed to save edge");

        public async Task<TNodeData?> LoadNodeAsync(TKey key) => await ExecuteWithErrorHandlingAsync(() =>
        {
            if (_nodesCollection == null) throw new InvalidOperationException("LiteDB persistence not initialized");
            return _nodesCollection.FindById(new BsonValue(key))?.Data;
        }, "Failed to load node");


        public async Task<TEdgeData?> LoadEdgeAsync(string edgeId) => await ExecuteWithErrorHandlingAsync(() =>
        {
            if (_edgesCollection == null) throw new InvalidOperationException("LiteDB persistence not initialized");
            return _edgesCollection.FindOne(x => x.EdgeId == edgeId)?.Data;
        }, "Failed to load edge");

        public async Task RemoveNodeAsync(TKey key) => await ExecuteWithErrorHandlingAsync(() =>
        {
            if (_nodesCollection == null || _edgesCollection == null) throw new InvalidOperationException("LiteDB persistence not initialized");
            _nodesCollection.Delete(new BsonValue(key));
            var edgesToRemove = _edgesCollection.FindAll()
                .Where(e => e.Source?.ToString() == key.ToString() || e.Target?.ToString() == key.ToString())
                .ToList();
            foreach (var edge in edgesToRemove)
            {
                _edgesCollection.Delete(edge.EdgeId);
            }
        }, "Failed to remove node");



        public async Task RemoveEdgeAsync(string edgeId) => await ExecuteWithErrorHandlingAsync(() =>
        {
            if (_edgesCollection == null) throw new InvalidOperationException("LiteDB persistence not initialized");
            _edgesCollection.DeleteMany(x => x.EdgeId == edgeId);
        }, "Failed to remove edge");

        public async Task<List<TKey>> GetAllNodeKeysAsync() => await ExecuteWithErrorHandlingAsync(() =>
        {
            if (_nodesCollection == null) throw new InvalidOperationException("LiteDB persistence not initialized");
            return _nodesCollection.FindAll().Select(x => x.Key).ToList();
        }, "Failed to get all node keys");

        public async Task<List<string>> GetAllEdgeIdsAsync() => await ExecuteWithErrorHandlingAsync(() =>
        {
            if (_edgesCollection == null) throw new InvalidOperationException("LiteDB persistence not initialized");
            return _edgesCollection.FindAll().Select(x => x.EdgeId).ToList();
        }, "Failed to get all edge ids");

        public async Task<bool> ContainsNodeAsync(TKey key) => await ExecuteWithErrorHandlingAsync(() =>
        {
            if (_nodesCollection == null) throw new InvalidOperationException("LiteDB persistence not initialized");
            return _nodesCollection.FindById(new BsonValue(key)) != null;
        }, "Failed to check if node exists");


        public async Task<bool> ContainsEdgeAsync(string edgeId) => await ExecuteWithErrorHandlingAsync(() =>
        {
            if (_edgesCollection == null) throw new InvalidOperationException("LiteDB persistence not initialized");
            return _edgesCollection.Exists(x => x.EdgeId == edgeId);
        }, "Failed to check if edge exists");

        public async Task ClearAllAsync() => await ExecuteWithErrorHandlingAsync(() =>
        {
            if (_nodesCollection == null || _edgesCollection == null) throw new InvalidOperationException("LiteDB persistence not initialized");
            _nodesCollection.DeleteAll();
            _edgesCollection.DeleteAll();
        }, "Failed to clear all data");

        public void Dispose()
        {
            if (!_disposed)
            {
                _database?.Dispose();
                _dbLock.Dispose();
                _disposed = true;
            }
        }
    }

    public class NodeDocument<TKey, TNodeData> where TKey : notnull where TNodeData : class
    {
        [BsonId] public TKey Key { get; set; } = default!;
        public TNodeData Data { get; set; } = default!;
        public string? Symbol { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class EdgeDocument<TKey, TEdgeData> where TKey : notnull where TEdgeData : class
    {
        [BsonId] public string EdgeId { get; set; } = default!;
        public TKey Source { get; set; } = default!;
        public TKey Target { get; set; } = default!;
        public bool IsDirected { get; set; }
        public TEdgeData? Data { get; set; }
        public double Weight { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}