
namespace TConfig.ContextFrame.Abstractions;

public interface IECSStorage
{

    int? GetEntityArchetypeId(int entityId);

    void SetEntityArchetypeId(int entityId, int archetypeId);

    void RemoveEntityArchetypeId(int entityId);

    IEnumerable<KeyValuePair<int, int>> GetAllEntityArchetypeIds();


    byte[]? GetArchetypeData(int signatureHash);

    void SetArchetypeData(int signatureHash, byte[] data);

    IEnumerable<int> GetAllArchetypeSignatureHashes();


    IEnumerable<int> GetFreeEntityIds();

    void AddFreeEntityId(int id);

    int? TakeFreeEntityId();

    int GetEntityVersion(int entityId);

    void SetEntityVersion(int entityId, int version);


    Array? GetChunkColumn(int chunkId, int componentTypeId);

    void SetChunkColumn(int chunkId, int componentTypeId, Array column);

    int[]? GetChunkEntityIds(int chunkId);

    void SetChunkEntityIds(int chunkId, int[] entityIds);
}
