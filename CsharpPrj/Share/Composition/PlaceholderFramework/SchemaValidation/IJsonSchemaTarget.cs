
namespace Common.PlaceholderFramework.SchemaValidation;

public interface IJsonSchemaTarget
{
    string TargetName { get; }

    string TargetType { get; }

    bool HasField(string fieldName);

    string? GetFieldValue(string fieldName);

    IEnumerable<IJsonSchemaTarget> GetChildren();
}
