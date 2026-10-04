
using WorkFlowMethod.Parameters.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.KeyMapping;
using IKeyMapper = ParameterResolver.Abstractions.IKeyMapper<string>;
using KeyMapperBuilder = ParameterResolver.KeyMapping.KeyMapperBuilder;

namespace WorkFlowMethod.Parameters;

public class ParameterKeyMapper : IParameterKeyMapper
{
    private readonly IKeyMapper? _keyMapper;

    public static readonly ParameterKeyMapper Default = new();

    public ParameterKeyMapper()
        : this(new KeyMapperBuilder.StringKeyMapper())
    {
    }

    public ParameterKeyMapper(IKeyMapper keyMapper)
    {
        _keyMapper = keyMapper ?? throw new ArgumentNullException(nameof(keyMapper));
    }

    public string AddHierarchyToKey(string key, string hierarchy)
    {
        return KeyProcessor.AddHierarchyToKey(key, hierarchy);
    }

    public List<string> AddHierarchyToKeys(List<string> keys, string hierarchy)
    {
        return KeyProcessor.AddHierarchyToKeys(keys, hierarchy);
    }

    public List<string> ParseKeysFromJson(string keysJson)
    {
        return _keyMapper!.ParseKeysFromJson(keysJson).ToList();
    }

    public string EnsureChainSuffix(string key, int index)
    {
        return KeyManager.EnsureChainSuffix(key, index);
    }

    public string GenerateCallkey(string symbol, int chainIndex, int nodeIndex, string? pattern = null)
    {
        return KeyManager.GenerateCallkey(symbol, chainIndex, nodeIndex, pattern: pattern);
    }

    public string GenerateReference(string reference, int chainIndex, string? pattern = null)
    {
        return KeyManager.GenerateReference(reference, chainIndex, pattern: pattern);
    }

    public int? ExtractChainIndex(string key)
    {
        return KeyManager.ExtractChainIndex(key);
    }

    public string ExtractHierarchyFromNode(object nodeConfig)
    {
        if (nodeConfig is Skeletons.NodeProcess.Models.NodeConfig config)
        {
            return KeyProcessor.ExtractHierarchyFromNode(config);
        }
        return string.Empty;
    }
}
