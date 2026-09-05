using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.Parsing.Rules; // 通用框架 JSON 条件判定内核（自 Agent.Core RuleEngine 提取，单源实现）

namespace Agent.Core.Tests
{
    /// <summary>
    /// 通用框架内核 <see cref="JsonRuleEngine"/> 回归基线（Agent.Core RuleEngine 现已委托本内核）。
    /// 直接锁定框架 API，含未知 op 日志回调重载；运算符/数组/数值语义与 RuleEngineTests 同源互证。
    /// </summary>
    [TestClass]
    [TestCategory("RuleEngine")]
    public class JsonRuleEngineTests
    {
        private static JsonElement Json(string s) => JsonSerializer.Deserialize<JsonElement>(s);

        [TestMethod]
        public void Evaluate_条件对象缺op_默认true_应命中()
        {
            var cond = Json("{\"field\":\"x\",\"value\":1}");
            Assert.IsTrue(JsonRuleEngine.EvaluateCondition(cond, Json("{\"x\":1}")));
        }

        [TestMethod]
        public void Evaluate_点路径字段_应命中()
        {
            // 内核依赖 Modules.Parsing.JsonPathHelper.NavigateTo 解析点路径。
            var data = Json("{\"exec_result\":{\"ok\":true}}");
            Assert.IsTrue(JsonRuleEngine.Evaluate(data, "exec_result.ok", "==", Json("true")));
        }

        [TestMethod]
        public void Evaluate_未知op_回调日志且返回false()
        {
            var data = Json("{\"x\":1}");
            string? cbOp = null;
            string? cbField = null;
            var result = JsonRuleEngine.Evaluate(data, "x", "===", Json("1"), (op, field) => { cbOp = op; cbField = field; });
            Assert.IsFalse(result);
            Assert.AreEqual("===", cbOp);
            Assert.AreEqual("x", cbField);
        }

        [TestMethod]
        public void Evaluate_未知op_无回调_静默返回false()
        {
            Assert.IsFalse(JsonRuleEngine.Evaluate(Json("{\"x\":1}"), "x", "===", Json("1")));
        }

        [TestMethod]
        public void EvaluateCondition_字符串false_应不命中()
        {
            Assert.IsFalse(JsonRuleEngine.EvaluateCondition(Json("\"false\""), Json("{\"x\":1}")));
        }
    }
}
