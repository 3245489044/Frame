using System.Text.Json;
using CallCenter.Core.Schema;
using CallCenter.Core.Tools;

namespace CallCenter.Tests;

/// <summary>锁定 P1 GetToolsAsync 派生规则：ToolDescriptor → Function schema，过滤 notExposedToModel（防递归）。</summary>
[TestClass]
public class ToolFunctionSchemaMapperTests
{
    private static ToolDescriptor Tool(string id, string name, ToolFlags flags, IReadOnlyDictionary<string, object?>? inputs = null)
        => new(id, name, ToolBackend.CSharp, 1, "desc: " + name,
            inputs ?? new Dictionary<string, object?>(),
            new Dictionary<string, object?>(),
            flags);

    private static Dictionary<string, object?> Param(string type, bool optional)
        => new() { ["type"] = type, ["optional"] = optional };

    [TestMethod]
    public void NotExposedToModel_Tools_Are_Filtered()
    {
        var exposed = Tool("tool:csharp:public_fn", "public_fn", ToolFlags.Deterministic);
        var internalTool = Tool("tool:csharp:internal", "internal_fn", ToolFlags.Deterministic | ToolFlags.NotExposedToModel);

        var schemas = ToolFunctionSchemaMapper.MapExposed(new[] { exposed, internalTool });

        Assert.AreEqual(1, schemas.Count);
        Assert.AreEqual("public_fn", schemas[0].Name);
    }

    [TestMethod]
    public void Params_Map_To_Json_Schema_With_Required()
    {
        var tool = Tool("tool:csharp:cc_add", "cc_add", ToolFlags.Deterministic, new Dictionary<string, object?>
        {
            ["a"] = Param("System.Double", false),
            ["b"] = Param("System.Double", false),
            ["note"] = Param("System.String", true),
        });

        var schema = ToolFunctionSchemaMapper.MapExposed(new[] { tool }).Single();

        var root = schema.InputSchema;
        Assert.AreEqual("object", root.GetProperty("type").GetString());
        var props = root.GetProperty("properties");
        Assert.AreEqual("number", props.GetProperty("a").GetProperty("type").GetString());
        Assert.AreEqual("string", props.GetProperty("note").GetProperty("type").GetString());

        var required = root.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();
        CollectionAssert.AreEquivalent(new[] { "a", "b" }, required);
    }

    [TestMethod]
    public void Empty_Input_Yields_Object_Schema_Without_Required()
    {
        var tool = Tool("tool:csharp:cc_utc_now", "cc_utc_now", ToolFlags.Deterministic);
        var schema = ToolFunctionSchemaMapper.MapExposed(new[] { tool }).Single();

        Assert.AreEqual(JsonValueKind.Object, schema.InputSchema.ValueKind);
        Assert.AreEqual("object", schema.InputSchema.GetProperty("type").GetString());
        Assert.IsFalse(schema.InputSchema.TryGetProperty("required", out _));
    }
}
