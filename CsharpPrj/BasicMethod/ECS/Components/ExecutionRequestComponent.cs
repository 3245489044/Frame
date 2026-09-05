
using System;
using System.Collections.Generic;
using ECS.Core;

namespace ECS.Components;

public struct ExecutionRequestComponent : IComponent
{
    public string FunctionName;

    public Dictionary<string, object?>? Parameters;

    public int SourceEntityId;

    public DateTime Timestamp;
}