
#nullable enable
using ECS.Core;

namespace FrontEndDriven.ECS.Components;

public struct ControlValueComponent : IComponent
{
    public string? Value { get; set; }
}