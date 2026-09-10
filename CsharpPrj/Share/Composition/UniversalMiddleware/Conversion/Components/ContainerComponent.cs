
#nullable enable
using TConfig.ECS;
using UniversalMiddleware.Abstractions;

namespace UniversalMiddleware.Conversion.Components;

public class ContainerComponent
{
    public string Name { get; set; } = "";

    public ContainerConfig? ContainerConfig { get; set; }

    public List<Entity> SubContainers { get; set; } = new();
}
