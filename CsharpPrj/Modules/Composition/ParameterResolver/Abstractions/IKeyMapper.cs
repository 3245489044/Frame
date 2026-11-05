
using ParameterResolver.KeyMapping;

namespace ParameterResolver.Abstractions;

public interface IKeyMapper<TKey>
{
    TKey Map(TKey sourceKey, TKey targetKey, KeyMappingConfig config);

    (IReadOnlyList<TKey> UpdatedKeys, IReadOnlyList<KeyMatchResult> MatchResults)
        MapBatch(IReadOnlyList<TKey> sourceKeys, IReadOnlyList<TKey> targetKeys, KeyMappingConfig config);

    IReadOnlyList<TKey> ParseKeysFromJson(string keysJson);

    IReadOnlyList<TKey> NormalizeKeys(IReadOnlyList<TKey> keys, KeyMappingConfig config);
}
