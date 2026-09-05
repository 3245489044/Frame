
using TConfig.ContextFrame.Abstractions;

namespace Modules.InternalStorage.Core;

public class ECSStorage : IECSStorage
{
    private readonly IStorageCell _cell;
    private readonly IStorageRegistry _registry;

    private const string EntityArchetypePrefix = "ecs:entity_archetype:";
    private const string ArchetypePrefix = "ecs:archetype:";
    private const string FreeEntityIdsKey = "ecs:free_entity_ids";
    private const string EntityVersionPrefix = "ecs:entity_version:";
    private const string ChunkColumnPrefix = "ecs:chunk_column:";
    private const string ChunkEntitiesPrefix = "ecs:chunk_entities:";

    public ECSStorage(IStorageCell cell, IStorageRegistry registry)
    {
        _cell = cell ?? throw new ArgumentNullException(nameof(cell));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }


    public int? GetEntityArchetypeId(int entityId)
    {
        var key = $"{EntityArchetypePrefix}{entityId}";
        return _cell.Contains(key, StorageLayer.Session)
            ? _cell.Get<int>(key, StorageLayer.Session)
            : null;
    }

    public void SetEntityArchetypeId(int entityId, int archetypeId)
    {
        _cell.Set($"{EntityArchetypePrefix}{entityId}", archetypeId, StorageLayer.Session);
    }

    public void RemoveEntityArchetypeId(int entityId)
    {
        _cell.Remove($"{EntityArchetypePrefix}{entityId}", StorageLayer.Session);
    }

    public IEnumerable<KeyValuePair<int, int>> GetAllEntityArchetypeIds()
    {
        var keys = _cell.GetKeys(StorageLayer.Session)
            .Where(k => k.StartsWith(EntityArchetypePrefix));
        foreach (var key in keys)
        {
            var archetypeId = _cell.Get<int>(key, StorageLayer.Session);
            var idStr = key[EntityArchetypePrefix.Length..];
            if (int.TryParse(idStr, out var entityId))
            {
                yield return new KeyValuePair<int, int>(entityId, archetypeId);
            }
        }
    }


    public byte[]? GetArchetypeData(int signatureHash)
    {
        return _cell.Get<byte[]>($"{ArchetypePrefix}{signatureHash}", StorageLayer.Session);
    }

    public void SetArchetypeData(int signatureHash, byte[] data)
    {
        _cell.Set($"{ArchetypePrefix}{signatureHash}", data, StorageLayer.Session);
    }

    public IEnumerable<int> GetAllArchetypeSignatureHashes()
    {
        return _cell.GetKeys(StorageLayer.Session)
            .Where(k => k.StartsWith(ArchetypePrefix))
            .Select(k => int.TryParse(k[ArchetypePrefix.Length..], out var hash) ? hash : -1)
            .Where(h => h >= 0);
    }


    public IEnumerable<int> GetFreeEntityIds()
    {
        return _cell.SetGetAll<int>(FreeEntityIdsKey, StorageLayer.Session);
    }

    public void AddFreeEntityId(int id)
    {
        _cell.SetAdd(FreeEntityIdsKey, id, StorageLayer.Session);
    }

    public int? TakeFreeEntityId()
    {
        if (_cell.SetCount(FreeEntityIdsKey, StorageLayer.Session) == 0)
            return null;

        var first = _cell.SetFirst<int>(FreeEntityIdsKey, StorageLayer.Session);
        _cell.SetRemove(FreeEntityIdsKey, first, StorageLayer.Session);
        return first;
    }

    public int GetEntityVersion(int entityId)
    {
        return _cell.Get<int>($"{EntityVersionPrefix}{entityId}", StorageLayer.Session);
    }

    public void SetEntityVersion(int entityId, int version)
    {
        _cell.Set($"{EntityVersionPrefix}{entityId}", version, StorageLayer.Session);
    }


    public Array? GetChunkColumn(int chunkId, int componentTypeId)
    {
        return _cell.Get<Array>($"{ChunkColumnPrefix}{chunkId}:{componentTypeId}", StorageLayer.Session);
    }

    public void SetChunkColumn(int chunkId, int componentTypeId, Array column)
    {
        _cell.Set($"{ChunkColumnPrefix}{chunkId}:{componentTypeId}", column, StorageLayer.Session);
    }

    public int[]? GetChunkEntityIds(int chunkId)
    {
        return _cell.Get<int[]>($"{ChunkEntitiesPrefix}{chunkId}", StorageLayer.Session);
    }

    public void SetChunkEntityIds(int chunkId, int[] entityIds)
    {
        _cell.Set($"{ChunkEntitiesPrefix}{chunkId}", entityIds, StorageLayer.Session);
    }
}
