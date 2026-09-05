
using System;
using ECS.Core;

namespace ECS.Components;

public struct ExecutionResultComponent : IComponent
{
    public string FunctionName;

    public object? Result;

    public bool Success;

    public string? ErrorMessage;

    public int SourceEntityId;

    public DateTime RequestTimestamp;

    public DateTime CompletedTimestamp;
}
