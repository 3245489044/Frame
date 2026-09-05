using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Agent.Contract.Llm;
using Strategy.DecisionRules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Core.Tests
{
    /// <summary>Decision Rules Engine 单测：匹配（contains/regex/role/exact/wildcard/jsonPath）+ 条件组合（AND/OR/not）
    /// + 优先级（declared）+ 兜底 + 输出（text/tool_call）+ 模板渲染 + 审计。复用 Plate.JsonMethod（JsonPathHelper/TemplateFormatter）。</summary>
    [TestClass]
    public class DecisionRulesEngineTests
    {
        private static readonly DecisionRulesEngine Engine = new();

        private static DecisionRuleSet Ordered(params DecisionRule[] rules)
            => new() { Id = "t", Priority = RuleSetPriorityMode.Ordered, Rules = rules };

        private static DecisionRule Rule(string match, OutputClause then, string? id = null)
            => new()
            {
                Id = id,
                When = new WhenCondition
                {
                    All = new[] { new MatchCondition { Match = new MatchClause { Strategy = MatchStrategy.Contains, Value = match } } },
                },
                Then = then,
            };

        private static DecisionRuleContext Ctx(string user, string system = "sys", IReadOnlyList<RuleMessage>? msgs = null)
            => new()
            {
                System = system,
                User = user,
                Messages = msgs ?? new[] { new RuleMessage("system", system), new RuleMessage("user", user) },
            };

        [TestMethod]
        public void Contains_Text_MatchesAndRenders()
        {
            var rs = Ordered(Rule("创建任务", new OutputClause { Type = OutputKind.Text, Template = "命中 {user}" }));
            var r = Engine.Evaluate(rs, Ctx("请创建任务"));
            Assert.IsTrue(r.Matched);
            Assert.AreEqual("命中 请创建任务", r.Text);
        }

        [TestMethod]
        public void Regex_Match()
        {
            var rs = Ordered(new DecisionRule
            {
                When = new WhenCondition { All = new[] { new MatchCondition { Match = new MatchClause { Strategy = MatchStrategy.Regex, Value = "调度|决策" } } } },
                Then = new OutputClause { Type = OutputKind.Text, Template = "调度命中" },
            });
            Assert.IsTrue(Engine.Evaluate(rs, Ctx("做一个调度决策")).Matched);
            Assert.IsFalse(Engine.Evaluate(rs, Ctx("纯闲聊")).Matched);
        }

        [TestMethod]
        public void Role_Match_PresenceAndSubstring()
        {
            var msgs = new[]
            {
                new RuleMessage("system", "你是助手"),
                new RuleMessage("assistant", "好的，我来处理"),
                new RuleMessage("user", "请继续"),
            };
            var presence = new DecisionRule
            {
                When = new WhenCondition { All = new[] { new MatchCondition { Match = new MatchClause { Field = "role:assistant", Strategy = MatchStrategy.Role } } } },
                Then = new OutputClause { Type = OutputKind.Text, Template = "assistant 存在" },
            };
            var sub = new DecisionRule
            {
                When = new WhenCondition { All = new[] { new MatchCondition { Match = new MatchClause { Field = "role:assistant", Strategy = MatchStrategy.Role, Value = "处理" } } } },
                Then = new OutputClause { Type = OutputKind.Text, Template = "子串命中" },
            };
            Assert.IsTrue(Engine.Evaluate(Ordered(presence), Ctx("u", msgs: msgs)).Matched);
            Assert.IsTrue(Engine.Evaluate(Ordered(sub), Ctx("u", msgs: msgs)).Matched);
        }

        [TestMethod]
        public void Declared_Priority_SelectsHigher()
        {
            var low = Rule("创建", new OutputClause { Type = OutputKind.Text, Template = "low" }, "low");
            low.Priority = 1;
            var high = Rule("创建", new OutputClause { Type = OutputKind.Text, Template = "high" }, "high");
            high.Priority = 10;
            var rs = new DecisionRuleSet
            {
                Id = "t",
                Priority = RuleSetPriorityMode.Declared,
                Rules = new[] { low, high },
            };
            var r = Engine.Evaluate(rs, Ctx("请创建任务"));
            Assert.AreEqual("high", r.Text);
            Assert.AreEqual("high", r.MatchedRule!.Id);
        }

        [TestMethod]
        public void Any_And_Not_Composition()
        {
            var any = new DecisionRule
            {
                When = new WhenCondition
                {
                    Any = new[]
                    {
                        new WhenCondition { All = new[] { new MatchCondition { Match = new MatchClause { Strategy = MatchStrategy.Contains, Value = "A" } } } },
                        new WhenCondition { All = new[] { new MatchCondition { Match = new MatchClause { Strategy = MatchStrategy.Contains, Value = "B" } } } },
                    },
                },
                Then = new OutputClause { Type = OutputKind.Text, Template = "any-hit" },
            };
            var notRule = new DecisionRule
            {
                When = new WhenCondition
                {
                    All = new[] { new MatchCondition { Not = new MatchCondition { Match = new MatchClause { Strategy = MatchStrategy.Contains, Value = "禁止" } } } },
                },
                Then = new OutputClause { Type = OutputKind.Text, Template = "not-hit" },
            };

            Assert.IsTrue(Engine.Evaluate(Ordered(any), Ctx("A 开头的")).Matched);
            Assert.IsTrue(Engine.Evaluate(Ordered(any), Ctx("B 开头的")).Matched);
            Assert.IsFalse(Engine.Evaluate(Ordered(any), Ctx("C 开头的")).Matched);
            Assert.IsTrue(Engine.Evaluate(Ordered(notRule), Ctx("正常内容")).Matched);
            Assert.IsFalse(Engine.Evaluate(Ordered(notRule), Ctx("含禁止词")).Matched);
        }

        [TestMethod]
        public void NoMatch_FallsBackToDefault()
        {
            var rs = Ordered(Rule("创建", new OutputClause { Type = OutputKind.Text, Template = "rule" }));
            rs.Default = new DecisionRule { Id = "d", Then = new OutputClause { Type = OutputKind.Text, Template = "兜底：{user}" } };
            var r = Engine.Evaluate(rs, Ctx("随便聊聊"));
            Assert.IsTrue(r.IsFallback);
            Assert.AreEqual("兜底：随便聊聊", r.Text);
        }

        [TestMethod]
        public void ToolCall_Output()
        {
            var rs = Ordered(Rule("创建任务", new OutputClause { Type = OutputKind.ToolCall, ToolName = "create_node_reference", Arguments = "{\"k\":1}" }));
            var r = Engine.Evaluate(rs, Ctx("请创建任务"));
            Assert.AreEqual(OutputKind.ToolCall, r.Kind);
            Assert.IsNotNull(r.ToolCalls);
            Assert.AreEqual("create_node_reference", r.ToolCalls![0].Name);
            Assert.AreEqual("{\"k\":1}", r.ToolCalls[0].Arguments);
        }

        [TestMethod]
        public void JsonPath_Match_ReusesJsonMethod()
        {
            var userJson = "{\"action\":\"continue\",\"nested\":{\"flag\":true}}";
            var rs = Ordered(new DecisionRule
            {
                When = new WhenCondition
                {
                    All = new[] { new MatchCondition { Match = new MatchClause { Field = "user", Strategy = MatchStrategy.JsonPath, Value = "$.nested.flag", Options = new Dictionary<string, object?> { ["equals"] = "true" } } } },
                },
                Then = new OutputClause { Type = OutputKind.Json, Template = "{\"hit\":1}" },
            });
            var r = Engine.Evaluate(rs, Ctx(userJson));
            Assert.IsTrue(r.Matched);
            Assert.AreEqual(OutputKind.Json, r.Kind);
        }

        [TestMethod]
        public void TemplateRenderer_RoleAndLastPlaceholders()
        {
            var msgs = new[]
            {
                new RuleMessage("system", "SYS"),
                new RuleMessage("assistant", "第一条助手"),
                new RuleMessage("user", "USER2"),
                new RuleMessage("assistant", "最后助手"),
            };
            var rs = Ordered(Rule("最后", new OutputClause { Type = OutputKind.Text, Template = "{last_assistant}|{role:user}|{last_message}" }));
            var r = Engine.Evaluate(rs, Ctx("任意内容", msgs: msgs));
            Assert.AreEqual("最后助手|USER2|最后助手", r.Text);
        }

        [TestMethod]
        public void Audit_RecordsHit()
        {
            var entries = new List<RuleAuditEntry>();
            var engine = new DecisionRulesEngine(audit: new CollectAudit(entries));
            var rs = Ordered(Rule("创建", new OutputClause { Type = OutputKind.Text, Template = "hit" }, "r1"));
            engine.Evaluate(rs, Ctx("请创建"));
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("r1", entries[0].RuleId);
            Assert.IsFalse(entries[0].IsFallback);
        }

        [TestMethod]
        public void FileLoader_LoadsRulesFile()
        {
            var path = Path.Combine(TestPath(), "local-llm.rules.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                {
                  "id": "file-rs",
                  "priority": "ordered",
                  "rules": [
                    { "id": "r1", "when": { "all": [ { "match": { "strategy": "contains", "value": "创建任务" } } ] }, "then": { "type": "text", "template": "文件命中" } }
                  ]
                }
                """);
            try
            {
                var loader = new FileRuleSetLoader(path);
                var rs = loader.Load();
                Assert.IsNotNull(rs);
                Assert.AreEqual("file-rs", rs!.Id);
                Assert.AreEqual(1, rs.Rules.Count);
                Assert.AreEqual("r1", rs.Rules[0].Id);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void FileLoader_MissingFile_ReturnsNull()
        {
            var loader = new FileRuleSetLoader(Path.Combine(TestPath(), "missing.rules.json"));
            Assert.IsNull(loader.Load());
        }

        [TestMethod]
        public void Validator_RejectsInvalidRegex()
        {
            var errors = new RuleSetJsonValidator().ValidateJson("""
                { "rules": [ { "id": "r-bad", "when": { "all": [ { "match": { "strategy": "regex", "value": "[" } } ] }, "then": { "type": "text", "template": "x" } } ] }
                """);
            Assert.IsTrue(errors.Count > 0, "坏正则应被 lint 显式报错");
            StringAssert.Contains(errors[0], "正则无效");
        }

        [TestMethod]
        public void Validator_AcceptsGoodFile()
        {
            var errors = new RuleSetJsonValidator().ValidateJson("""
                { "id": "ok", "rules": [ { "id": "r1", "then": { "type": "tool_call", "toolName": "t" } } ] }
                """);
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void LocalLlmClient_FileRules_Delegates()
        {
            var path = Path.Combine(TestPath(), "client.rules.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                { "id": "c", "rules": [ { "id": "cr", "when": { "all": [ { "match": { "strategy": "contains", "value": "文件任务" } } ] }, "then": { "type": "text", "template": "来自文件" } } ] }
                """);
            try
            {
                var client = new LocalLlmClient(new LocalLlmOptions { RulesFile = path });
                var r = client.ChatAsync("sys", "请处理文件任务").GetAwaiter().GetResult();
                Assert.AreEqual("来自文件", r.Content);
                // CallLog 记录命中规则 id（文件规则）。
                Assert.AreEqual("cr", client.CallLog.Last().MatchedRuleId);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void Router_ReusesEngine_ForNonLlmDecisionPoint()
        {
            // 第二决策点演示：工具选择路由（非 LocalLlm），用引擎把输入文本 → 目标工具键。
            var routeSet = new DecisionRuleSet
            {
                Id = "tool-route",
                Priority = RuleSetPriorityMode.Ordered,
                Rules = new[]
                {
                    new DecisionRule
                    {
                        Id = "route-to-create",
                        When = new WhenCondition { All = new[] { new MatchCondition { Match = new MatchClause { Strategy = MatchStrategy.Contains, Value = "创建任务" } } } },
                        Then = new OutputClause { Type = OutputKind.Text, Template = "create_node_reference" },
                    },
                    new DecisionRule
                    {
                        Id = "route-to-query",
                        When = new WhenCondition { All = new[] { new MatchCondition { Match = new MatchClause { Strategy = MatchStrategy.Contains, Value = "查询" } } } },
                        Then = new OutputClause { Type = OutputKind.Text, Template = "query_node" },
                    },
                },
                Default = new DecisionRule { Id = "route-default", Then = new OutputClause { Type = OutputKind.Text, Template = "fallback" } },
            };

            var router = new DecisionRuleRouter(routeSet);
            var create = router.Route("请创建任务 A");
            Assert.IsTrue(create.Matched);
            Assert.AreEqual("route-to-create", create.RuleId);
            Assert.AreEqual("create_node_reference", create.Text);

            var query = router.Route("查询节点状态");
            Assert.AreEqual("route-to-query", query.RuleId);
            Assert.AreEqual("query_node", query.Text);

            var unknown = router.Route("随便说点什么");
            Assert.IsTrue(unknown.IsFallback);
            Assert.AreEqual("fallback", unknown.Text);
        }

        private static string TestPath()
        {
            var dir = Path.Combine(Path.GetTempPath(), "decision-rules-tests");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private sealed class CollectAudit : IRuleAuditSink
        {
            private readonly List<RuleAuditEntry> _entries;
            public CollectAudit(List<RuleAuditEntry> entries) => _entries = entries;
            public void Record(RuleAuditEntry entry) => _entries.Add(entry);
        }
    }
}
