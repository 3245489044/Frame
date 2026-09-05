
using System.Text.Json.Nodes;
using Modules.Pipeline;
using TConfig;
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;

namespace Common.PlaceholderFramework.TemplateExpansion.Converters;

public abstract class JsonConverterBase : IJsonConverter
{
    public string Name { get; protected set; }

    public int Order { get; protected set; }

    public bool IsFatal { get; protected set; }

    public bool Enabled { get; set; } = true;

    protected JsonConverterBase(string name, int order = 100, bool isFatal = false)
    {
        Name = name;
        Order = order;
        IsFatal = isFatal;
    }

    public object? Process(object input, ErrorCollector errors, PipelineContext context)
    {
        if (!Enabled)
            return input;

        if (input is null)
        {
            errors.Add($"{Name.ToUpper()}_NULL_INPUT",
                $"Converter '{Name}' received null input",
                IsFatal ? ErrorLevel.Fatal : ErrorLevel.Error);
            return IsFatal ? null : input;
        }

        if (input is JsonObject obj)
        {
            try
            {
                return Convert(obj, errors, context) ?? input;
            }
            catch (Exception ex)
            {
                errors.Add($"{Name.ToUpper()}_EXCEPTION",
                    $"Converter '{Name}' threw exception: {ex.Message}",
                    IsFatal ? ErrorLevel.Fatal : ErrorLevel.Error,
                    ex);
                return IsFatal ? null : input;
            }
        }

        var actualType = input.GetType().Name;
        errors.Add($"{Name.ToUpper()}_TYPE_MISMATCH",
            $"Converter '{Name}' expected JsonObject input, got {actualType}. " +
            $"尝试类型修复...",
            IsFatal ? ErrorLevel.Fatal : ErrorLevel.Warning);

        if (input is string str)
        {
            try
            {
                var reparsed = System.Text.Json.Nodes.JsonNode.Parse(str)?.AsObject();
                if (reparsed != null)
                {
                    return Convert(reparsed, errors, context) ?? reparsed;
                }
            }
            catch
            {
            }
        }

        return IsFatal ? null : input;
    }

    protected abstract JsonObject? Convert(JsonObject json, ErrorCollector errors, PipelineContext context);

    #region 循环引用检测

    protected static bool PushAndCheckCircular(PipelineContext context, string stackKey, string name, ErrorCollector errors)
    {
        var stack = GetOrCreateStack(context, stackKey);
        if (stack.Contains(name))
        {
            errors.Add($"{stackKey.ToUpper()}_CIRCULAR_REFERENCE",
                $"Circular reference detected: '{name}' is already in resolution stack: {string.Join(" -> ", stack)}",
                ErrorLevel.Error);
            return true;
        }
        stack.Push(name);
        return false;
    }

    protected static void PopFromStack(PipelineContext context, string stackKey)
    {
        var stack = GetOrCreateStack(context, stackKey);
        if (stack.Count > 0)
            stack.Pop();
    }

    private static Stack<string> GetOrCreateStack(PipelineContext context, string stackKey)
    {
        var fullKey = $"ResolutionStack_{stackKey}";
        var existing = context.GetItem<Stack<string>>(fullKey);
        if (existing != null)
            return existing;

        var stack = new Stack<string>();
        context.SetItem(fullKey, stack);
        return stack;
    }

    #endregion
}
