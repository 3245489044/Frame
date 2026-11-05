
using System;
using Modules.Registry.AutoRegister;

using DelegateType = System.Delegate;

namespace Modules.Registry.AutoRegister.Wrappers;

public interface IDelegateWrapper
{
    string WrapperType { get; }

    DelegateType Wrap(RegisteredItemInfo item);
}

public class WrapperConfig
{
    public string Type { get; set; } = "passthrough";

    public string? ParamParsing { get; set; }

    public string? ResultFormat { get; set; }
}
