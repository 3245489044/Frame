namespace Modules.Parsing.Models;

public class WorldModel
{
    public string AdapterId { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> State { get; set; } = new();
    public List<EntityDescriptor> Entities { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class EntityDescriptor
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
    public List<string> SupportedActions { get; set; } = new();
}
