
using System.Collections.Concurrent;
using ECS.Core;

namespace ECS.Components;

public struct GlobalFunctionRegistryComponent : IComponent
{
    public ConcurrentDictionary<string, FunctionRegistration>? Functions;
}

public class FunctionRegistration
{
    public string Name { get; init; }

    public Delegate Handler { get; init; }

    public string Description { get; init; }
}