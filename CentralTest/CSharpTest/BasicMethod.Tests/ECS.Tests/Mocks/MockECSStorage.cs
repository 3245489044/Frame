
using TConfig.ContextFrame.Abstractions;

namespace ECS.Tests.Mocks;

public class MockECSStorage : IECSStorage
{
    private readonly Dictionary<int, int> _entityArchetypes = new();

    private readonly Dictionary<int, byte[]> _archetypeData = new();

    private readonly SortedSet<int> _freeEntityIds = new();

    private readonly Dictionary<int, int> _entityVersions = new();

    private readonly Dictionary<int, Dictionary<int, Array>> _chunkColumns = new();

    private readonly Dictionary<int, int[]> _chunkEntityIds = new();


    public int? GetEntityArchetypeId(int entityId)
    {
        return _entityArchetypes.TryGetValue(entityId, out var archetypeId) ? archetypeId : null;
    }

    public void SetEntityArchetypeId(int entityId, int archetypeId)
    {
        _entityArchetypes[entityId] = archetypeId;
    }

    public void RemoveEntityArchetypeId(int entityId)
    {
        _entityArchetypes.Remove(entityId);
    }

    public IEnumerable<KeyValuePair<int, int>> GetAllEntityArchetypeIds()
    {
        return _entityArchetypes;
    }


    public byte[]? GetArchetypeData(int signatureHash)
    {
        return _archetypeData.TryGetValue(signatureHash, out var data) ? data : null;
    }

    public void SetArchetypeData(int signatureHash, byte[] data)
    {
        _archetypeData[signatureHash] = data;
    }

    public IEnumerable<int> GetAllArchetypeSignatureHashes()
    {
        return _archetypeData.Keys;
    }


    public IEnumerable<int> GetFreeEntityIds()
    {
        return _freeEntityIds;
    }

    public void AddFreeEntityId(int id)
    {
        _freeEntityIds.Add(id);
    }

    public int? TakeFreeEntityId()
    {
        if (_freeEntityIds.Count == 0)
            return null;

        var id = _freeEntityIds.Min;
        _freeEntityIds.Remove(id);
        return id;
    }

    public int GetEntityVersion(int entityId)
    {
        return _entityVersions.TryGetValue(entityId, out var version) ? version : 0;
    }

    public void SetEntityVersion(int entityId, int version)
    {
        _entityVersions[entityId] = version;
    }


    public Array? GetChunkColumn(int chunkId, int componentTypeId)
    {
        if (_chunkColumns.TryGetValue(chunkId, out var columns))
        {
            return columns.TryGetValue(componentTypeId, out var column) ? column : null;
        }
        return null;
    }

    public void SetChunkColumn(int chunkId, int componentTypeId, Array column)
    {
        if (!_chunkColumns.ContainsKey(chunkId))
        {
            _chunkColumns[chunkId] = new Dictionary<int, Array>();
        }
        _chunkColumns[chunkId][componentTypeId] = column;
    }

    public int[]? GetChunkEntityIds(int chunkId)
    {
        return _chunkEntityIds.TryGetValue(chunkId, out var ids) ? ids : null;
    }

    public void SetChunkEntityIds(int chunkId, int[] entityIds)
    {
        _chunkEntityIds[chunkId] = entityIds;
    }

    public void Clear()
    {
        _entityArchetypes.Clear();
        _archetypeData.Clear();
        _freeEntityIds.Clear();
        _entityVersions.Clear();
        _chunkColumns.Clear();
        _chunkEntityIds.Clear();
    }
}
