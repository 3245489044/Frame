using CallCenter.Mcp;

namespace CallCenter.Tests;

/// <summary>
/// C1 回归锚点：MCP 返回文本的 TH-9 宽容封包（McpResult）——可解析 JSON → 结构化值、非 JSON 文本 → 原样字符串。
/// 锁定 mcp_call_tool result 语义，防止该判定在端点各自实现产生分叉/失真。
/// </summary>
[TestClass]
public class McpResultTests
{
    [TestMethod]
    public void Structured_Json_Object_Comes_Back_As_JsonElement()
    {
        var v = McpResult.CoerceStructured("{\"a\":1,\"b\":\"x\"}");
        Assert.IsNotNull(v);
        Assert.IsInstanceOfType(v, typeof(System.Text.Json.JsonElement));
        var el = (System.Text.Json.JsonElement)v!;
        Assert.AreEqual(1, el.GetProperty("a").GetInt32());
        Assert.AreEqual("x", el.GetProperty("b").GetString());
    }

    [TestMethod]
    public void NonJson_Plain_Text_Comes_Back_As_Original_String()
    {
        const string plain = "工具输出的任意文本，非 JSON";
        var v = McpResult.CoerceStructured(plain);
        Assert.IsInstanceOfType(v, typeof(string));
        Assert.AreEqual(plain, v);
    }

    [TestMethod]
    public void Json_Quoted_String_Comes_Back_As_JsonElement_String()
    {
        // 注意：JsonSerializer.Deserialize<object> 对合法 JSON 一律装箱为 JsonElement（含字符串标量），
        // 这是原 mcp_call_tool 行为的真实语义——故字符串 JSON 也呈 JsonElement，而非 C# string。
        var v = McpResult.CoerceStructured("\"hi\"");
        Assert.IsNotNull(v);
        Assert.IsInstanceOfType(v, typeof(System.Text.Json.JsonElement));
        Assert.AreEqual("hi", ((System.Text.Json.JsonElement)v!).GetString());
    }
}
