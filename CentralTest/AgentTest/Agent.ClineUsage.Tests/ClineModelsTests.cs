using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Modules;

namespace Agent.Modules.Tests
{
    /// <summary>
    /// ClineModels 纯单元测试：验证 JSON 反序列化、计算属性与运行选项默认值。
    /// 不依赖 node / cli.js，可离线运行。
    /// </summary>
    [TestClass]
    [TestCategory("Modules_Unit")]
    public class ClineModelsTests
    {
        // ---- ClineRunResult ----

        [TestMethod]
        public void ClineRunResult_成功JSON_应反序列化并具备计算属性()
        {
            const string json = """
            {
              "ok": true,
              "exitCode": 0,
              "messages": [
                { "type": "say", "text": "开始执行", "ts": 1720000000000, "say": "tool" },
                { "type": "result", "text": "翻译完成", "ts": 1720000001000 }
              ],
              "resultText": "翻译完成",
              "sessionId": "sess-123",
              "error": null
            }
            """;

            var result = JsonSerializer.Deserialize<ClineRunResult>(json, JsonTestOptions());

            Assert.IsNotNull(result, "成功 JSON 应反序列化出实例");
            Assert.IsTrue(result!.Ok, "ok=true 应解析为 true");
            Assert.AreEqual(0, result.ExitCode, "exitCode 应为 0");
            Assert.IsNotNull(result.Messages, "messages 不应为 null");
            Assert.AreEqual(2, result.Messages!.Count, "messages 应包含 2 条");
            Assert.AreEqual("翻译完成", result.ResultText, "resultText 应解析");
            Assert.AreEqual("sess-123", result.SessionId, "sessionId 应解析");

            Assert.AreEqual("翻译完成", result.Text, "Text 应取 ResultText");
            Assert.IsTrue(result.Succeeded, "Succeeded 应等于 Ok");
            Assert.IsNull(result.Failure, "成功时 Failure 应为 null");
        }

        [TestMethod]
        public void ClineRunResult_失败JSON_应正确设置Failure()
        {
            const string json = """
            {
              "ok": false,
              "exitCode": 1,
              "messages": null,
              "resultText": null,
              "sessionId": null,
              "error": "API 密钥无效"
            }
            """;

            var result = JsonSerializer.Deserialize<ClineRunResult>(json, JsonTestOptions());

            Assert.IsNotNull(result, "失败 JSON 应反序列化出实例");
            Assert.IsFalse(result!.Succeeded, "Succeeded 应为 false");
            Assert.IsNotNull(result.Failure, "失败时 Failure 不应为 null");
            Assert.AreEqual("API 密钥无效", result.Failure, "Failure 应取 Error");
            Assert.AreEqual(string.Empty, result.Text, "无 resultText 时 Text 应为空字符串");
        }

        [TestMethod]
        public void ClineRunResult_注释与尾随逗号_应容忍()
        {
            const string json = """
            {
              // 注释行
              "ok": true,
              "exitCode": 0,
              "messages": [],
              "resultText": "ok",
            }
            """;

            var result = JsonSerializer.Deserialize<ClineRunResult>(json, JsonTestOptions());

            Assert.IsNotNull(result, "含注释与尾随逗号的 JSON 应可解析");
            Assert.IsTrue(result!.Ok, "ok 应为 true");
            Assert.AreEqual("ok", result.Text, "Text 应为 ok");
            Assert.IsNotNull(result.Messages, "空 messages 应解析为非 null 空列表");
        }

        [TestMethod]
        public void ClineRunResult_字段名大小写不敏感()
        {
            const string json = """
            {
              "OK": true,
              "EXITCODE": 0,
              "ResultText": "大小写无关"
            }
            """;

            var result = JsonSerializer.Deserialize<ClineRunResult>(json, JsonTestOptions());

            Assert.IsNotNull(result, "大写字段名应被解析");
            Assert.IsTrue(result!.Ok, "OK 大写应映射到 Ok");
            Assert.AreEqual("大小写无关", result.Text, "ResultText 大写应映射");
        }

        // ---- ClineMessage ----

        [TestMethod]
        public void ClineRunResult_ArchivedMessages_应反序列化且可再序列化为归档形状()
        {
            // 模拟 FsSink 捕获的全量消息（磁盘零残留场景，daemon 终帧回带的 archivedMessages）。
            const string json = """
            {
              "ok": true,
              "exitCode": 0,
              "messages": [],
              "resultText": "ok",
              "sessionId": "sess-arch",
              "archivedMessages": [
                { "role": "user", "content": "你好" },
                { "role": "assistant", "content": "你好！" }
              ]
            }
            """;

            var result = JsonSerializer.Deserialize<ClineRunResult>(json, JsonTestOptions());

            Assert.IsNotNull(result, "含 archivedMessages 的 JSON 应反序列化");
            Assert.IsNotNull(result!.ArchivedMessages, "archivedMessages 不应为 null");
            Assert.AreEqual(2, result.ArchivedMessages!.Count, "archivedMessages 应包含 2 条全量消息");

            // 归档落库形状：与 ClineDaemonEndpoint.ResolveArchiveMessagesJson 的主源序列化一致（{ messages: [...] }）。
            var archiveJson = JsonSerializer.Serialize(new { messages = result.ArchivedMessages });
            var parsed = JsonSerializer.Deserialize<JsonElement>(archiveJson, JsonTestOptions());
            Assert.IsTrue(parsed.TryGetProperty("messages", out var msgs), "归档 JSON 应含 messages 键");
            Assert.AreEqual(2, msgs.GetArrayLength(), "messages 数组长度应为 2");
        }

        [TestMethod]
        public void ClineMessage_各字段_应反序列化()
        {
            const string json = """
            {
              "type": "ask",
              "text": "确认操作？",
              "ts": 1720000000000,
              "say": null,
              "ask": "confirm",
              "reasoning": "需要用户确认",
              "partial": true
            }
            """;

            var message = JsonSerializer.Deserialize<ClineMessage>(json, JsonTestOptions());

            Assert.IsNotNull(message, "消息 JSON 应反序列化");
            Assert.AreEqual("ask", message!.Type, "type 应解析");
            Assert.AreEqual("确认操作？", message.Text, "text 应解析");
            Assert.AreEqual(1720000000000L, message.Ts, "ts 应解析");
            Assert.IsNull(message.Say, "null say 应解析为 null");
            Assert.AreEqual("confirm", message.Ask, "ask 应解析");
            Assert.AreEqual("需要用户确认", message.Reasoning, "reasoning 应解析");
            Assert.IsTrue(message.Partial, "partial 应解析为 true");
        }

        // ---- ClineRunOptions ----

        [TestMethod]
        public void ClineRunOptions_默认值_应全为null()
        {
            var options = new ClineRunOptions();

            Assert.IsNull(options.Provider, "Provider 默认应为 null");
            Assert.IsNull(options.Model, "Model 默认应为 null");
            Assert.IsNull(options.TimeoutSec, "TimeoutSec 默认应为 null");
            Assert.IsNull(options.AutoApprove, "AutoApprove 默认应为 null");
            Assert.IsNull(options.SessionId, "SessionId 默认应为 null");
            Assert.IsNull(options.ApiKey, "ApiKey 默认应为 null");
        }

        [TestMethod]
        public void ClineRunOptions_位置参数与with复制_应生效()
        {
            var options = new ClineRunOptions(
                Provider: "deepseek",
                Model: "deepseek-v4-flash",
                Cwd: @"D:\repo",
                TimeoutSec: 300,
                AutoApprove: true);

            Assert.AreEqual("deepseek", options.Provider, "Provider 应生效");
            Assert.AreEqual("deepseek-v4-flash", options.Model, "Model 应生效");
            Assert.AreEqual(@"D:\repo", options.Cwd, "Cwd 应生效");
            Assert.AreEqual(300, options.TimeoutSec, "TimeoutSec 应生效");
            Assert.AreEqual(true, options.AutoApprove, "AutoApprove 应生效");

            var copied = options with { SessionId = "sess-9" };
            Assert.AreEqual("sess-9", copied.SessionId, "with 应更新 SessionId");
            Assert.AreEqual("deepseek", copied.Provider, "with 应保留 Provider");
            Assert.IsNull(options.SessionId, "原对象 SessionId 应保持不变");
        }

        // ---- ClineCommandResult ----

        [TestMethod]
        public void ClineCommandResult_Version字段_应反序列化()
        {
            const string json = """
            {
              "ok": true,
              "exitCode": 0,
              "error": null,
              "version": "1.0.0"
            }
            """;

            var result = JsonSerializer.Deserialize<ClineCommandResult>(json, JsonTestOptions());

            Assert.IsNotNull(result, "命令结果 JSON 应反序列化");
            Assert.IsTrue(result!.Ok, "ok 应为 true");
            Assert.AreEqual("1.0.0", result.Version, "version 应解析");
        }

        private static JsonSerializerOptions JsonTestOptions() => new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
    }
}
