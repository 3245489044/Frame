
using System.Text.Json;
using WorkFlowMethod.Parameters.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using IKeyMapper = ParameterResolver.Abstractions.IKeyMapper<string>;
using KeyMapperBuilder = ParameterResolver.KeyMapping.KeyMapperBuilder;

namespace WorkFlowMethod.Parameters;

public class ParameterConfigProvider : IParameterConfigProvider
{
    private readonly IKeyMapper? _keyMapper;

    public static readonly ParameterConfigProvider Default = new();

    public ParameterConfigProvider(IKeyMapper? keyMapper = null)
    {
        _keyMapper = keyMapper ?? new KeyMapperBuilder.StringKeyMapper();
    }

    public List<string> GetParameterKeys(NodeConfig node)
    {
        if (node?.funcPipelineConfig == null)
            return new List<string>();

        return ParseKeysFromJson(node.funcPipelineConfig.ParameterKeys);
    }

    public List<string> GetResultKeys(NodeConfig node)
    {
        if (node?.funcPipelineConfig == null)
            return new List<string>();

        return ParseKeysFromJson(node.funcPipelineConfig.ResultKeys);
    }

    public void SetParameterKeys(NodeConfig node, List<string> keys)
    {
        if (node?.funcPipelineConfig == null) return;
        node.funcPipelineConfig.ParameterKeys = SerializeKeysToJson(keys);
    }

    public void SetResultKeys(NodeConfig node, List<string> keys)
    {
        if (node?.funcPipelineConfig == null) return;
        node.funcPipelineConfig.ResultKeys = SerializeKeysToJson(keys);
    }

    public string GetParameters(NodeConfig node)
    {
        if (node?.funcPipelineConfig?.Params == null)
            return string.Empty;

        return node.funcPipelineConfig.Params;
    }

    public List<string> ParseKeysFromJson(string keysJson)
    {
        if (string.IsNullOrEmpty(keysJson))
            return new List<string>();

        return _keyMapper!.ParseKeysFromJson(keysJson).ToList();
    }

    public string SerializeKeysToJson(List<string> keys)
    {
        if (keys == null || keys.Count == 0)
            return "[]";

        return JsonSerializer.Serialize(keys);
    }
}
