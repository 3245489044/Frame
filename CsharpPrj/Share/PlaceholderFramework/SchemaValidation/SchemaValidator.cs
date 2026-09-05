
namespace Common.PlaceholderFramework.SchemaValidation;

public class SchemaValidator
{

    public ValidationResult Validate(IJsonSchemaTarget target, JsonSchema schema, bool strictMode = true)
    {
        var result = new ValidationResult();

        if (target == null)
        {
            result.Errors.Add(new ValidationError(
                "(target)", "(null)",
                "校验目标为 null"));
            return result;
        }

        if (schema == null)
        {
            result.Errors.Add(new ValidationError(
                target.TargetName, target.TargetType,
                "Schema 定义为 null"));
            return result;
        }

        ValidateTarget(target, schema, result, strictMode);
        return result;
    }

    public ValidationResult Validate<TKey>(
        IJsonSchemaTarget target,
        SchemaRegistry<TKey> registry,
        TKey schemaKey,
        bool strictMode = true) where TKey : notnull
    {
        var schema = registry.GetSchema(schemaKey);
        return Validate(target, schema, strictMode);
    }

    public ValidationResult ValidateAll(
        IEnumerable<IJsonSchemaTarget> targets,
        JsonSchema schema,
        bool strictMode = true)
    {
        var result = new ValidationResult();

        if (targets == null)
        {
            result.Errors.Add(new ValidationError(
                "(targets)", "(null)",
                "校验目标集合为 null"));
            return result;
        }

        foreach (var target in targets)
        {
            var childResult = Validate(target, schema, strictMode);
            result.Merge(childResult);
        }

        return result;
    }

    public ValidationResult ValidateAll<TKey>(
        IEnumerable<IJsonSchemaTarget> targets,
        SchemaRegistry<TKey> registry,
        Func<IJsonSchemaTarget, TKey> keySelector,
        bool strictMode = true) where TKey : notnull
    {
        var result = new ValidationResult();

        if (targets == null)
        {
            result.Errors.Add(new ValidationError(
                "(targets)", "(null)",
                "校验目标集合为 null"));
            return result;
        }

        foreach (var target in targets)
        {
            var key = keySelector(target);
            var schema = registry.GetSchema(key);
            var childResult = Validate(target, schema, strictMode);
            result.Merge(childResult);
        }

        return result;
    }


    private static void ValidateTarget(
        IJsonSchemaTarget target,
        JsonSchema schema,
        ValidationResult result,
        bool strictMode)
    {
        result.TotalChecked++;

        ValidateRequiredFields(target, schema, result, strictMode);

        ValidateConditionalFields(target, schema, result, strictMode);

        ValidateFieldValues(target, schema, result, strictMode);

        ValidateChildren(target, schema, result, strictMode);
    }

    private static void ValidateRequiredFields(
        IJsonSchemaTarget target,
        JsonSchema schema,
        ValidationResult result,
        bool strictMode)
    {
        foreach (var requiredField in schema.RequiredFields)
        {
            if (!target.HasField(requiredField))
            {
                AddError(result, target.TargetName, target.TargetType,
                    $"缺少必填字段 '{requiredField}'。契约要求: {string.Join(", ", schema.RequiredFields)}",
                    strictMode);
            }
        }
    }

    private static void ValidateConditionalFields(
        IJsonSchemaTarget target,
        JsonSchema schema,
        ValidationResult result,
        bool strictMode)
    {
        foreach (var kvp in schema.ConditionalFields)
        {
            var condition = kvp.Key;
            var requiredWhenConditionMet = kvp.Value;

            if (IsConditionMet(target, condition))
            {
                foreach (var field in requiredWhenConditionMet)
                {
                    if (!target.HasField(field))
                    {
                        AddError(result, target.TargetName, target.TargetType,
                            $"条件 '{condition}' 满足时缺少必填字段 '{field}'",
                            strictMode);
                    }
                }
            }
        }
    }

    private static void ValidateFieldValues(
        IJsonSchemaTarget target,
        JsonSchema schema,
        ValidationResult result,
        bool strictMode)
    {
        if (schema.FieldAllowedValues.Count == 0)
            return;

        foreach (var kvp in schema.FieldAllowedValues)
        {
            var fieldName = kvp.Key;
            var allowedValues = kvp.Value;

            var fieldValue = target.GetFieldValue(fieldName);
            if (fieldValue == null)
                continue;

            if (!allowedValues.Contains(fieldValue) &&
                !allowedValues.Any(v => string.Equals(v, fieldValue, StringComparison.OrdinalIgnoreCase)))
            {
                var allowedStr = string.Join(", ", allowedValues.OrderBy(v => v));
                AddError(result, target.TargetName, target.TargetType,
                    $"字段 '{fieldName}' 的值 '{fieldValue}' 不合法。允许的值: {allowedStr}",
                    strictMode);
            }
        }
    }

    private static void ValidateChildren(
        IJsonSchemaTarget target,
        JsonSchema schema,
        ValidationResult result,
        bool strictMode)
    {
        foreach (var child in target.GetChildren())
        {
            JsonSchema childSchema;
            if (schema.NestedSchemas.Count > 0)
            {
                if (schema.NestedSchemas.TryGetValue(child.TargetType, out var nestedSchema))
                    childSchema = nestedSchema;
                else if (schema.NestedSchemas.TryGetValue(child.TargetName, out var namedSchema))
                    childSchema = namedSchema;
                else
                    childSchema = JsonSchema.CreateDefault();
            }
            else
            {
                childSchema = JsonSchema.CreateDefault();
            }

            ValidateTarget(child, childSchema, result, strictMode);
        }
    }


    private static void AddError(ValidationResult result, string targetName, string targetType,
        string message, bool strictMode)
    {
        var error = new ValidationError(targetName, targetType, message,
            strictMode ? ValidationSeverity.Error : ValidationSeverity.Warning);

        if (strictMode)
            result.Errors.Add(error);
        else
            result.Warnings.Add(error);
    }

    private static bool IsConditionMet(IJsonSchemaTarget target, string condition)
    {
        if (string.IsNullOrEmpty(condition) || target == null)
            return false;

        var parts = condition.Split('=', 2);
        if (parts.Length != 2)
            return false;

        var fieldName = parts[0].Trim();
        var expectedValue = parts[1].Trim();

        var actualValue = target.GetFieldValue(fieldName);
        return string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase);
    }
}
