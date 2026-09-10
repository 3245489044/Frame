
namespace Common.PlaceholderFramework.SchemaValidation;

public class JsonSchema
{
    public List<string> RequiredFields { get; set; } = new();

    public List<string> OptionalFields { get; set; } = new();

    public Dictionary<string, string[]> ConditionalFields { get; set; } = new();

    public Dictionary<string, HashSet<string>> FieldAllowedValues { get; set; } = new();

    public Dictionary<string, JsonSchema> NestedSchemas { get; set; } = new();

    [Obsolete("直接使用 new JsonSchema() 即可，此方法无额外逻辑")]
    public static JsonSchema CreateDefault() => new();
}
