using TConfig;

namespace FrontEndDriven.Shared;

public sealed record LoadedConfigInfo(UIConfiguration Config, DateTime LoadedAt)
{
    public string? WindowName { get; } = Config.Form?.Title;

    public LoadedConfigInfo(UIConfiguration config) : this(config, DateTime.Now)
    {
    }
}