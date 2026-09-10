
#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using UniversalMiddleware.Abstractions;
using UniversalMiddleware.Conversion.Components;
using UniversalMiddleware.Serialization;

namespace UniversalMiddleware.Conversion;

public class JsonToEntityConverter
{
    private readonly IJsonSerializer _serializer;

    public JsonToEntityConverter(IJsonSerializer? serializer = null)
    {
        _serializer = serializer ?? new UnifiedJsonSerializer();
    }

    public ContainerComponent ParseContainer(string json)
    {
        var node = JsonNode.Parse(json);
        return ParseContainerNode(node);
    }

    public ContainerComponent ParseContainerNode(JsonNode? node)
    {
        var container = new ContainerComponent();

        if (node is not JsonObject obj)
            return container;

        if (obj.TryGetPropertyValue("name", out var nameNode) && nameNode is JsonValue nameValue)
            container.Name = nameValue.GetValue<string>() ?? "";

        if (obj.TryGetPropertyValue("container", out var configNode) && configNode is JsonObject configObj)
        {
            container.ContainerConfig = new ContainerConfig
            {
                SourceType = configObj.TryGetPropertyValue("sourceType", out var typeNode) && typeNode is JsonValue typeVal
                    ? typeVal.GetValue<string>() ?? "static" : "static",
                DefaultConfig = configObj.TryGetPropertyValue("defaultConfig", out var defNode) && defNode is JsonValue defVal
                    ? defVal.GetValue<string>() : null,
                ReferenceKey = configObj.TryGetPropertyValue("referenceKey", out var refNode) && refNode is JsonValue refVal
                    ? refVal.GetValue<string>() : null,
                AutoLoad = configObj.TryGetPropertyValue("autoLoad", out var autoNode) && autoNode is JsonValue autoVal
                    ? autoVal.GetValue<bool>() : true,
                Description = configObj.TryGetPropertyValue("description", out var descNode) && descNode is JsonValue descVal
                    ? descVal.GetValue<string>() : null,
            };
        }

        if (obj.TryGetPropertyValue("children", out var childrenNode) && childrenNode is JsonArray childrenArray)
        {
            foreach (var childNode in childrenArray)
            {
                var childContainer = ParseContainerNode(childNode);
            }
        }

        return container;
    }

    public SplitContainerComponent ParseSplitContainer(string json)
    {
        var node = JsonNode.Parse(json);
        return ParseSplitContainerNode(node);
    }

    public SplitContainerComponent ParseSplitContainerNode(JsonNode? node)
    {
        var split = new SplitContainerComponent();

        if (node is not JsonObject obj)
            return split;

        if (obj.TryGetPropertyValue("orientation", out var orientNode) && orientNode is JsonValue orientVal)
            split.Orientation = orientVal.GetValue<string>() ?? "horizontal";

        if (obj.TryGetPropertyValue("splitterDistance", out var distNode) && distNode is JsonValue distVal)
            split.SplitterDistance = distVal.GetValue<int>();

        if (obj.TryGetPropertyValue("isFixed", out var fixedNode) && fixedNode is JsonValue fixedVal)
            split.IsFixed = fixedVal.GetValue<bool>();

        return split;
    }

    public List<ContainerComponent> ParseContainerList(string json)
    {
        var result = new List<ContainerComponent>();
        var node = JsonNode.Parse(json);

        if (node is JsonArray array)
        {
            foreach (var item in array)
                result.Add(ParseContainerNode(item));
        }
        else if (node is JsonObject)
        {
            result.Add(ParseContainerNode(node));
        }

        return result;
    }
}
