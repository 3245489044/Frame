
namespace Common.PlaceholderFramework.SchemaValidation;

public class JsonSchemaBuilder
{
    private readonly JsonSchema _schema = new();


    public JsonSchemaBuilder WithRequired(params string[] fields)
    {
        _schema.RequiredFields.AddRange(fields);
        return this;
    }

    public JsonSchemaBuilder WithOptional(params string[] fields)
    {
        _schema.OptionalFields.AddRange(fields);
        return this;
    }

    public JsonSchemaBuilder WithConditional(string condition, string[] fields)
    {
        _schema.ConditionalFields[condition] = fields;
        return this;
    }

    public JsonSchemaBuilder WithAllowedValues(string field, HashSet<string> allowedValues)
    {
        _schema.FieldAllowedValues[field] = allowedValues;
        return this;
    }

    public JsonSchemaBuilder WithNested(string field, JsonSchema nestedSchema)
    {
        _schema.NestedSchemas[field] = nestedSchema;
        return this;
    }

    public JsonSchema Build() => _schema;


    [Obsolete("直接使用 new JsonSchemaBuilder() 即可，此方法无额外逻辑")]
    public static JsonSchemaBuilder BaseObject()
    {
        return new JsonSchemaBuilder();
    }

    public static JsonSchemaBuilder EnumField(string field, HashSet<string> allowedValues)
    {
        return new JsonSchemaBuilder()
            .WithRequired(field)
            .WithAllowedValues(field, allowedValues);
    }
}
