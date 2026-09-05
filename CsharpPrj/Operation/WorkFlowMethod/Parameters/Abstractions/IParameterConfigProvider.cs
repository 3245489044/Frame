
using WorkFlowMethod.Skeletons.NodeProcess.Models;

namespace WorkFlowMethod.Parameters.Abstractions;

public interface IParameterConfigProvider
{
    List<string> GetParameterKeys(NodeConfig node);

    List<string> GetResultKeys(NodeConfig node);

    void SetParameterKeys(NodeConfig node, List<string> keys);

    void SetResultKeys(NodeConfig node, List<string> keys);

    string GetParameters(NodeConfig node);

    List<string> ParseKeysFromJson(string keysJson);

    string SerializeKeysToJson(List<string> keys);
}
