using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Agent.Axioms.DataContainers;
using Agent.Axioms.Infrastructure;
using Agent.Axioms.StateAdapters;
using Agent.Contract.Data;
using Agent.Contract.DataContainers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Operation.StateBlock.Core;
using ParameterResolver.KeyMapping;

namespace Agent.Axioms.Tests.DataContainers
{
    /// <summary>DataContainerBinder 核心行为测试（T0-T6）。</summary>
    [TestClass]
    public sealed class DataContainerBinderTests
    {
        private static StateCenter NewCenter() => new(new StorageCell("test"));
        private static StateContextAdapter NewAdapter(StateCenter center) => new(center, "t", "s", "r1");
        private static readonly DataContainerBinder Binder = DataContainerBinder.Instance;

        private static KeyMappingConfig Shorthand(string json)
            => JsonSerializer.Deserialize<KeyMappingConfig>(json, new JsonSerializerOptions
            {
                Converters = { KeyMappingConfigJsonConverter.Instance },
            })!;

        [TestMethod]
        public void T0_ZeroConfig_IsEmpty()
        {
            var center = NewCenter();
            var state = NewAdapter(center);

            var seed = Binder.BindSeedPhase(state, new BindingContainer());
            Assert.IsTrue(seed.IsEmpty);

            var step = new BindingStep { ParameterKeys = new[] { "exec_result" }, ResultKeys = new[] { "exec_result" } };
            var parameters = new Dictionary<string, object>();
            var input = Binder.BindStepInputPhase(state, step, parameters, null);
            Assert.IsTrue(input.IsEmpty);
            Assert.IsFalse(parameters.ContainsKey("exec_result"));

            var harvest = Binder.BindHarvestPhase(state, new BindingContainer());
            Assert.IsTrue(harvest.IsEmpty);
        }

        [TestMethod]
        public void T1_ShorthandDict_InputMapping_Fills()
        {
            var center = NewCenter();
            var state = NewAdapter(center);
            state.SetTransient("final_text", "hello");

            var mapping = Shorthand("{\"final_text\":\"exec_result\"}");
            var step = new BindingStep { ParameterKeys = new[] { "exec_result" }, InputMapping = mapping };
            var parameters = new Dictionary<string, object>();

            var diag = Binder.BindStepInputPhase(state, step, parameters, null);

            Assert.AreEqual("hello", parameters["exec_result"]);
            Assert.AreEqual(1, diag.MatchedKeyCount);
        }

        [TestMethod]
        public void T2_FullExactForm_EquivalentToShorthand()
        {
            var center = NewCenter();
            var state = NewAdapter(center);
            state.SetTransient("final_text", "hello");

            var mapping = Shorthand("{\"Rules\":[{\"RuleType\":\"Exact\",\"SourcePattern\":\"final_text\",\"TargetPattern\":\"exec_result\"}]}");
            var step = new BindingStep { ParameterKeys = new[] { "exec_result" }, InputMapping = mapping };
            var parameters = new Dictionary<string, object>();

            Binder.BindStepInputPhase(state, step, parameters, null);

            Assert.AreEqual("hello", parameters["exec_result"]);
        }

        [TestMethod]
        public void T3_PatternRule_Matches()
        {
            var center = NewCenter();
            var state = NewAdapter(center);
            state.SetTransient("text_final", "v");

            var mapping = KeyMappingConfig.FromRules(new MappingRule
            {
                RuleType = MappingRuleType.Pattern,
                SourcePattern = "*_final",
                TargetPattern = "*_output",
            });
            var step = new BindingStep { ParameterKeys = new[] { "text_output" }, InputMapping = mapping };
            var parameters = new Dictionary<string, object>();

            var diag = Binder.BindStepInputPhase(state, step, parameters, null);

            Assert.AreEqual("v", parameters["text_output"]);
            Assert.IsTrue(diag.MatchedKeyCount > 0);
        }
        [TestMethod]
        public void T4_MultiLevelRule_MapsCrossDomain()
        {
            var center = NewCenter();
            var state = NewAdapter(center);
            state.SetTransient("x.y", "v");

            var mapping = new KeyMappingConfig();
            mapping.Rules.Add(new MappingRule
            {
                RuleType = MappingRuleType.MultiLevel,
                RequireAllLevels = true,
                LevelPatterns =
                {
                    new LevelPattern { TargetPattern = "a", SourcePattern = "x" },
                    new LevelPattern { TargetPattern = "b", SourcePattern = "y" },
                },
            });
            var step = new BindingStep { ParameterKeys = new[] { "a.b" }, InputMapping = mapping };
            var parameters = new Dictionary<string, object>();

            var diag = Binder.BindStepInputPhase(state, step, parameters, null);

            Assert.AreEqual("v", parameters["a.b"]);
            Assert.IsTrue(diag.MatchedKeyCount > 0);
        }

        [TestMethod]
        public void T5_PriorityMerge_NodeWins()
        {
            var center = NewCenter();
            var state = NewAdapter(center);
            state.SetTransient("src_a", "A");
            state.SetTransient("src_b", "B");

            var nodeMapping = Shorthand("{\"src_b\":\"param\"}");
            var globalMapping = Shorthand("{\"src_a\":\"param\"}");
            var step = new BindingStep { ParameterKeys = new[] { "param" }, InputMapping = nodeMapping };
            var parameters = new Dictionary<string, object>();

            Binder.BindStepInputPhase(state, step, parameters, globalMapping);

            // 节点级优先命中：param 取 src_b = "B"
            Assert.AreEqual("B", parameters["param"]);
        }

        [TestMethod]
        public void T6_ConflictDetection_KeepsOriginal()
        {
            var center = NewCenter();
            var state = NewAdapter(center);
            state.SetTransient("exec_result", "old");

            var step = new BindingStep { ResultKeys = new[] { "exec_result" } };
            var resultProps = new Dictionary<string, object?> { ["exec_result"] = "new" };

            var diag = Binder.BindStepOutputPhase(state, step, resultProps, null);

            Assert.AreEqual(1, diag.Conflicts.Count);
            Assert.AreEqual("old", state.GetTransient<string>("exec_result"));
        }
    }
}
