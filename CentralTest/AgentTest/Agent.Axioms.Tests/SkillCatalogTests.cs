using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Agent.Axioms.Prompts;
using Agent.Axioms.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Skill;

namespace Agent.Axioms.Tests.Skills
{
    /// <summary>外部挂载技能框架测试：目录加载 / 零 LLM 选择 / 技能块装配。</summary>
    [TestClass]
    public sealed class SkillCatalogTests
    {
        [TestMethod]
        public void LoadFromDirectory_ParsesFrontMatterAndBody()
        {
            using var tmp = new TempDir();
            WriteSkill(tmp.Root, "create-task", "task; create; ur_tasks", "CreateTaskGraphRequest",
                "创建任务操作指南正文");
            WriteSkill(tmp.Root, "connect-nodes", "node; connect; edge", "ReferenceEdge",
                "连接节点操作指南正文");

            var all = SkillCatalog.LoadFromDirectory(tmp.Root);

            Assert.AreEqual(2, all.Count);
            var task = all.First(s => s.Name == "create-task");
            Assert.AreEqual("operation", task.Kind);
            Assert.IsTrue(task.Tags.Contains("ur_tasks"));
            Assert.AreEqual("CreateTaskGraphRequest", task.Schema);
            Assert.IsTrue(task.Body.Contains("创建任务操作指南正文"), "正文应含 body");
            Assert.IsFalse(task.Body.Contains("name:"), "body 不应含 front-matter");
        }

        [TestMethod]
        public void Select_MatchByKeywordAndCap()
        {
            using var tmp = new TempDir();
            WriteSkill(tmp.Root, "create-task", "task; ur_tasks", "", "t");
            WriteSkill(tmp.Root, "connect-nodes", "node; edge", "", "c");
            WriteSkill(tmp.Root, "create-reference", "reference; ur_references", "", "r");
            var all = SkillCatalog.LoadFromDirectory(tmp.Root);

            var originalMax = SkillsOptions.MaxInjected;
            try
            {
                SkillsOptions.MaxInjected = 2; // 命中 2 个仍截断到 2，未命中的 create-reference 不注入
                var selected = SkillSelector.Select(all, goal: "create-task connect-nodes", userInput: "", taskKey: "");

                Assert.AreEqual(2, selected.Count, "仅命中且被上限截断的技能入选");
                var names = selected.Select(s => s.Name).ToHashSet();
                Assert.IsTrue(names.Contains("create-task") && names.Contains("connect-nodes"));
                Assert.IsFalse(names.Contains("create-reference"), "未命中的技能不得注入");
            }
            finally
            {
                SkillsOptions.MaxInjected = originalMax;
            }
        }

        [TestMethod]
        public void Select_ExplicitNames_FollowOrder_IgnoreUnknown()
        {
            using var tmp = new TempDir();
            WriteSkill(tmp.Root, "create-task", "task", "", "t");
            WriteSkill(tmp.Root, "connect-nodes", "node", "", "c");
            var all = SkillCatalog.LoadFromDirectory(tmp.Root);

            var selected = SkillSelector.Select(all, goal: "", userInput: "", taskKey: "",
                requestedNames: new[] { "connect-nodes", "no-such-skill", "create-task" });

            Assert.AreEqual(2, selected.Count);
            Assert.AreEqual("connect-nodes", selected[0].Name, "显式顺序优先");
            Assert.AreEqual("create-task", selected[1].Name);
        }

        [TestMethod]
        public void Select_NoMatch_ReturnsEmpty()
        {
            using var tmp = new TempDir();
            WriteSkill(tmp.Root, "create-task", "task", "", "t");
            var all = SkillCatalog.LoadFromDirectory(tmp.Root);

            var selected = SkillSelector.Select(all, goal: "毫无关系的目标", userInput: "", taskKey: "");
            Assert.AreEqual(0, selected.Count);
        }

        [TestMethod]
        public void Assemble_Empty_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, SkillPromptAssembler.Build(Array.Empty<SkillDefinition>()));
            Assert.AreEqual(string.Empty, SkillPromptAssembler.Build(null!));
        }

        [TestMethod]
        public void Assemble_IncludesHeaderAndSkillBodies()
        {
            var block = SkillPromptAssembler.Build(new[]
            {
                new SkillDefinition { Name = "create-task", Description = "创建任务", Body = "调用 TaskGraphService" },
            });

            Assert.IsTrue(block.Contains("技能操作指南"));
            Assert.IsTrue(block.Contains("## Skill: create-task"));
            Assert.IsTrue(block.Contains("TaskGraphService"));
        }

        [TestMethod]
        public void LoadFromDirectory_ParsesExecutorField()
        {
            using var tmp = new TempDir();
            WriteSkill(tmp.Root, "conversation-answer", "conversation; answer", "", "经 mcp_call_tool 调 answer", executor: "answer");
            WriteSkill(tmp.Root, "conversation-cline", "conversation; cline", "", "经 mcp_call_tool 调 cline", executor: "cline");
            WriteSkill(tmp.Root, "default-executor", "default", "", "无 executor 声明");

            var all = SkillCatalog.LoadFromDirectory(tmp.Root);

            Assert.AreEqual("answer", all.First(s => s.Name == "conversation-answer").Executor);
            Assert.AreEqual("cline", all.First(s => s.Name == "conversation-cline").Executor);
            Assert.AreEqual("sk", all.First(s => s.Name == "default-executor").Executor, "缺省 executor=sk");
        }


        [TestMethod]
        public void LoadFromDirectory_ParsesModuleFields()
        {
            using var tmp = new TempDir();
            WriteSkill(tmp.Root, "contract-envelope", "contract", "", "内部契约正文", module: "internal");
            WriteSkill(tmp.Root, "graph-write", "graph; task", "", "确定性写图正文", module: "mcp", backend: "TaskGraphService");
            WriteSkill(tmp.Root, "axiom-gen", "axiom", "", "生成器指导", module: "rare");
            WriteSkill(tmp.Root, "legacy", "legacy", "", "无 module 旧技能", executor: "sk");

            var all = SkillCatalog.LoadFromDirectory(tmp.Root);

            var env = all.First(s => s.Name == "contract-envelope");
            Assert.AreEqual(SkillModule.Internal, env.Module);
            Assert.IsFalse(env.OnDemand);

            var graph = all.First(s => s.Name == "graph-write");
            Assert.AreEqual(SkillModule.Mcp, graph.Module);
            Assert.AreEqual("TaskGraphService", graph.Backend);
            Assert.IsFalse(graph.OnDemand);

            var axiom = all.First(s => s.Name == "axiom-gen");
            Assert.AreEqual(SkillModule.Rare, axiom.Module);
            Assert.IsTrue(axiom.OnDemand, "module==rare 缺省即按需注入");

            var legacy = all.First(s => s.Name == "legacy");
            Assert.AreEqual(SkillModule.Internal, legacy.Module, "无 module/kind=operation/executor=sk → Internal 缺省推断");
        }

        [TestMethod]
        public void Select_OnDemandExcludedFromAutoMatch_ButExplicitNameWorks()
        {
            using var tmp = new TempDir();
            WriteSkill(tmp.Root, "hot-skill", "common, task", "", "热技能", module: "mcp");
            WriteSkill(tmp.Root, "rare-skill", "common, axiom", "", "非常用技能", module: "rare");

            var all = SkillCatalog.LoadFromDirectory(tmp.Root);

            // 关键词含 common：热技能命中；rare OnDemand 不参与自动匹配
            var auto = SkillSelector.Select(all, goal: "common", userInput: "", taskKey: "");
            Assert.IsTrue(auto.Any(s => s.Name == "hot-skill"));
            Assert.IsFalse(auto.Any(s => s.Name == "rare-skill"), "OnDemand 技能不得被关键词自动匹配");

            // 显式点名可命中 OnDemand
            var explicitSel = SkillSelector.Select(all, goal: "", userInput: "", taskKey: "", requestedNames: new[] { "rare-skill" });
            Assert.AreEqual(1, explicitSel.Count);
            Assert.AreEqual("rare-skill", explicitSel[0].Name);
        }

        [TestMethod]
        public void SelectWithRare_ExplicitOnly()
        {
            using var tmp = new TempDir();
            WriteSkill(tmp.Root, "hot-skill", "common", "", "热技能");
            WriteSkill(tmp.Root, "rare-skill", "common, rare", "", "非常用", module: "rare");

            var all = SkillCatalog.LoadFromDirectory(tmp.Root);
            var hot = all.Where(s => s.Module != SkillModule.Rare).ToArray();
            var rare = all.Where(s => s.Module == SkillModule.Rare).ToArray();

            // 关键词 common 只从热集匹配；rare 不参与
            var sel = SkillSelector.SelectWithRare(hot, rare, goal: "common", userInput: "", taskKey: "");
            Assert.IsTrue(sel.Any(s => s.Name == "hot-skill"));
            Assert.IsFalse(sel.Any(s => s.Name == "rare-skill"));

            // 显式点名 rare 命中（SelectWithRare 支持按需注入）
            var explicitSel = SkillSelector.SelectWithRare(hot, rare, goal: "", userInput: "", taskKey: "",
                requestedNames: new[] { "rare-skill" });
            Assert.AreEqual(1, explicitSel.Count);
            Assert.AreEqual("rare-skill", explicitSel[0].Name);
        }

        [TestMethod]
        public void LoadFromDirectory_Recursive_ModuleSubfolders()
        {
            using var tmp = new TempDir();
            // 三模块物理分组：internal/mcp/rare 子文件夹，递归扫描应发现全部技能
            WriteSkill(Path.Combine(tmp.Root, "internal"), "contract-envelope", "contract", "", "内部契约", module: "internal");
            WriteSkill(Path.Combine(tmp.Root, "mcp"), "graph-write", "graph", "", "确定性写图", module: "mcp", backend: "TaskGraphService");
            WriteSkill(Path.Combine(tmp.Root, "rare"), "axiom-gen", "axiom", "", "生成器", module: "rare");

            var all = SkillCatalog.LoadFromDirectory(tmp.Root);

            Assert.AreEqual(3, all.Count, "递归扫描应发现三模块子文件夹内全部技能");
            Assert.IsTrue(all.Any(s => s.Name == "contract-envelope" && s.Module == SkillModule.Internal));
            Assert.IsTrue(all.Any(s => s.Name == "graph-write" && s.Module == SkillModule.Mcp && s.Backend == "TaskGraphService"));
            Assert.IsTrue(all.Any(s => s.Name == "axiom-gen" && s.Module == SkillModule.Rare));
        }

        [TestMethod]
        public void LoadFromDirectory_ParsesL0ModuleAndAlwaysOn()
        {
            using var tmp = new TempDir();
            var dir = Path.Combine(tmp.Root, "prompt-l0");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"),
                "---\nname: prompt-l0\nkind: l0\nmodule: l0\n---\n");

            var all = SkillCatalog.LoadFromDirectory(tmp.Root);

            Assert.AreEqual(1, all.Count);
            var l0 = all[0];
            Assert.AreEqual(SkillModule.L0, l0.Module);
            Assert.IsTrue(l0.AlwaysOn, "module: l0 缺省应恒在注入（AlwaysOn=true）");
        }

        [TestMethod]
        public void LoadFromDirectory_L0ExplicitAlwaysFalse_RespectsFlag()
        {
            using var tmp = new TempDir();
            var dir = Path.Combine(tmp.Root, "prompt-l0");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"),
                "---\nname: prompt-l0\nkind: l0\nmodule: l0\nalways: false\n---\n");

            var l0 = SkillCatalog.LoadFromDirectory(tmp.Root).Single();
            Assert.AreEqual(SkillModule.L0, l0.Module);
            Assert.IsFalse(l0.AlwaysOn, "显式 always: false 应覆盖缺省推断");
        }

        [TestMethod]
        public void LoadFromDirectory_ParsesDslType()
        {
            using var tmp = new TempDir();
            var dir = Path.Combine(tmp.Root, "l0-format");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"),
                "---\nname: l0-format\nkind: l0\nmodule: l0\ndsl: format\n---\n# 格式契约\n");

            var all = SkillCatalog.LoadFromDirectory(tmp.Root);

            Assert.AreEqual(1, all.Count);
            var s = all[0];
            Assert.AreEqual(SkillModule.L0, s.Module);
            Assert.AreEqual("format", s.Dsl, "dsl: front-matter 应解析到 Dsl 字段");
            StringAssert.Contains(s.Body, "# 格式契约");
        }

        [TestMethod]
        public void LoadFromDirectory_DefaultDslIsNone()
        {
            using var tmp = new TempDir();
            WriteSkill(tmp.Root, "create-task", "task", "", "t");

            var s = SkillCatalog.LoadFromDirectory(tmp.Root).Single();
            Assert.AreEqual("none", s.Dsl, "缺省 dsl 应为 none（普通操作技能）");
        }

        [TestMethod]
        public void PromptDslSource_ReadsMountedDslSkillBody()
        {
            var originalHot = Agent.State.Skills.SkillRuntimeState.Hot;
            var originalRare = Agent.State.Skills.SkillRuntimeState.Rare;
            try
            {
                Agent.State.Skills.SkillRuntimeState.Set(
                    new[]
                    {
                        new Agent.Skill.SkillDefinition
                        {
                            Name = "l1l2",
                            Module = Agent.Skill.SkillModule.Internal,
                            Dsl = "protocol",
                            Body = "## L1/L2 协议",
                        },
                    },
                    Array.Empty<Agent.Skill.SkillDefinition>());

                var text = PromptDslSource.ReadSkillBody("protocol");
                Assert.AreEqual("## L1/L2 协议", text, "应取到 dsl 类型匹配的技能正文");
                Assert.IsNull(PromptDslSource.ReadSkillBody("format"), "未挂载对应 dsl → null");
            }
            finally
            {
                Agent.State.Skills.SkillRuntimeState.Set(originalHot, originalRare);
            }
        }

        private static void WriteSkill(
            string root, string name, string tags, string schema, string body,
            string executor = "sk", string? module = null, string? backend = null, bool onDemand = false)
        {
            var dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"name: {name}");
            sb.AppendLine($"description: {name} 说明");
            sb.AppendLine("kind: operation");
            sb.AppendLine($"module: {module ?? "internal"}");
            if (!string.IsNullOrWhiteSpace(backend)) sb.AppendLine($"backend: {backend}");
            if (onDemand) sb.AppendLine("on_demand: true");
            sb.AppendLine($"tags: [{tags}]");
            sb.AppendLine($"trigger: {name}");
            sb.AppendLine($"schema: {schema}");
            sb.AppendLine($"executor: {executor}");
            sb.AppendLine("---");
            sb.AppendLine($"# Skill: {name}");
            sb.AppendLine();
            sb.AppendLine(body);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), sb.ToString());
        }

        /// <summary>测试临时目录（用后即删）。</summary>
        private sealed class TempDir : IDisposable
        {
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "skills_test_" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Root);
            public void Dispose()
            {
                try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); } catch { /* 忽略清理失败 */ }
            }
        }
    }
}
