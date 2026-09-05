
namespace WorkFlowMethod.Parameters.Abstractions;

public interface IParameterKeyMapper
{
    string AddHierarchyToKey(string key, string hierarchy);

    List<string> AddHierarchyToKeys(List<string> keys, string hierarchy);

    List<string> ParseKeysFromJson(string keysJson);

    string EnsureChainSuffix(string key, int index);

    string GenerateCallkey(string symbol, int chainIndex, int nodeIndex, string? pattern = null);

    string GenerateReference(string reference, int chainIndex, string? pattern = null);

    int? ExtractChainIndex(string key);

    string ExtractHierarchyFromNode(object nodeConfig);
}
