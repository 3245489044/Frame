
using System.Collections.Concurrent;

namespace FrontEndDriven.Shared;

public class ConfigLoadInput
{
    public ConcurrentDictionary<string, LoadedConfigInfo>? LoadedConfigs { get; set; }
}
