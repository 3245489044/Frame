using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation;
using Share.Conversation.ContextBinding;

namespace Share.Conversation.Tests;

[TestClass]
public class ContextBindingTests
{
    [TestMethod]
    public void AnchorKey_Formats()
    {
        Assert.AreEqual("prompt.l0", AnchorKey.PromptL0.Value);
        Assert.AreEqual("prompt.l1", AnchorKey.PromptL1.Value);
        Assert.AreEqual("prompt.l2", AnchorKey.PromptL2.Value);
        Assert.AreEqual("skill.block", AnchorKey.SkillCombined.Value);
        Assert.AreEqual("skill.cv_mgmt.block", AnchorKey.SkillBlock("cv_mgmt").Value);
        Assert.AreEqual("history.3", AnchorKey.History(3).Value);
        Assert.AreEqual("wf.x.out", AnchorKey.Node("wf.x.out").Value, "节点锚点原样取节点键（SplitWfKey 可解析）");
        Assert.IsTrue(AnchorKey.From("").IsEmpty);
    }

    [TestMethod]
    public void PromptSnapshotBuilder_Build_EmitsBindingsForNonEmptySegments()
    {
        var snap = PromptSnapshotBuilder.Build("L0...", "skill block...", "L1...", "L2...", new[] { "cv_mgmt", "cv_mgmt" });
        Assert.AreEqual("L0...", snap.L0);
        Assert.AreEqual("L1...", snap.L1);
        Assert.AreEqual("L2...", snap.L2);
        Assert.AreEqual("skill block...", snap.SkillBlock);
        Assert.AreEqual(1, snap.SelectedSkills.Count, "重名技能去重后仅保留一个");
        Assert.AreEqual(1, snap.SelectedSkills.Distinct().Count(), "去重后唯一");

        // 四段均非空 → 4 条 Binding（l0/l1/l2/skill.block）
        Assert.AreEqual(4, snap.Bindings.Count);
        Assert.IsNotNull(snap.BindingFor(AnchorKey.PromptL0));
        Assert.IsNotNull(snap.BindingFor(AnchorKey.PromptL1));
        Assert.IsNotNull(snap.BindingFor(AnchorKey.PromptL2));
        Assert.IsNotNull(snap.BindingFor(AnchorKey.SkillCombined));

        // 每条 Binding 的哈希与源文本一致（I3 哈希可复算）
        var l0 = snap.BindingFor(AnchorKey.PromptL0);
        Assert.AreEqual(PromptSnapshotBuilder.Hash("L0..."), l0!.TextHash);
        Assert.AreEqual(MessageOrigin.LlmSystem, l0.Origin);
        Assert.AreEqual(FeedTarget.System, l0.FeedTarget);
    }

    [TestMethod]
    public void PromptSnapshotBuilder_Build_SkipsEmptySegments()
    {
        var snap = PromptSnapshotBuilder.Build("L0...", "", "L1...", "", Array.Empty<string>());
        Assert.AreEqual(2, snap.Bindings.Count, "空段（SkillBlock/L2）不产生 Binding");
        Assert.IsNull(snap.BindingFor(AnchorKey.SkillCombined), "空 SkillBlock 无 Binding");
        Assert.IsNull(snap.BindingFor(AnchorKey.PromptL2), "空 L2 无 Binding");
        Assert.IsNotNull(snap.BindingFor(AnchorKey.PromptL0));
        Assert.IsNotNull(snap.BindingFor(AnchorKey.PromptL1));
    }

    [TestMethod]
    public void PromptSnapshot_System_JoinsL0AndSkillBlock()
    {
        var snap = PromptSnapshotBuilder.Build("L0", "SK", "", "", null);
        Assert.AreEqual("L0\n\nSK", snap.System, "system 槽位 = L0 + SkillBlock，空行分隔（与 PromptText.Assemble 一致）");
    }

    [TestMethod]
    public void PromptSnapshotBuilder_Hash_IsDeterministic()
    {
        Assert.AreEqual(PromptSnapshotBuilder.Hash("abc"), PromptSnapshotBuilder.Hash("abc"), "哈希确定性");
        Assert.AreNotEqual(PromptSnapshotBuilder.Hash("abc"), PromptSnapshotBuilder.Hash("abd"));
        Assert.AreEqual(64, PromptSnapshotBuilder.Hash("abc").Length, "SHA256 十六进制 64 位");
    }

    [TestMethod]
    public void PromptSnapshot_ToJson_FromJson_RoundTrip()
    {
        var snap = PromptSnapshotBuilder.Build("L0", "SK", "L1", "L2", new[] { "cv", "ax" });
        var json = snap.ToJson();
        var restored = PromptSnapshot.FromJson(json);
        Assert.IsNotNull(restored);
        Assert.AreEqual(snap.L0, restored!.L0);
        Assert.AreEqual(snap.SkillBlock, restored.SkillBlock);
        Assert.AreEqual(snap.L1, restored.L1);
        Assert.AreEqual(snap.L2, restored.L2);
        CollectionAssert.AreEqual(snap.SelectedSkills.ToArray(), restored.SelectedSkills.ToArray());
        // 反序列化重建 Bindings（含 system 段）
        Assert.IsNotNull(restored.BindingFor(AnchorKey.PromptL0));
        Assert.IsNotNull(restored.BindingFor(AnchorKey.SkillCombined));
        Assert.AreEqual(snap.System, restored.System, "system 槽位（L0+SkillBlock）往返一致");
    }

    [TestMethod]
    public void PromptSnapshot_FromJson_Invalid_ReturnsNull()
    {
        Assert.IsNull(PromptSnapshot.FromJson(null));
        Assert.IsNull(PromptSnapshot.FromJson(""));
        Assert.IsNull(PromptSnapshot.FromJson("not-json"));
    }

    [TestMethod]
    public void Canonical_Llm_Ui_BidirectionalConsistent_NoViolations()
    {
        // P4：Canonical → Llm → Ui 双向逐字一致 + 零额外（R1/R2/R3/R4/R5/R6 全绿）。
        var snapshot = PromptSnapshotBuilder.Build("L0全文", "SK块", "L1填槽", "", new[] { "cv" });

        // Canonical（history + 节点产出）
        var canonical = new List<ConversationMessage>
        {
            Msg(AnchorKey.History(1), "你好"),
            Msg(AnchorKey.History(2), "好的"),
            Msg(AnchorKey.Node("wf.x.out"), "节点产出"),
        };

        // LlmView = canonical 历史 + system 段（从同一份 __prompt_snapshot 构造）
        var llm = new List<ConversationMessage>(canonical)
        {
            SystemMsg(AnchorKey.PromptL0, snapshot.L0),
            SystemMsg(AnchorKey.SkillCombined, snapshot.SkillBlock),
            SystemMsg(AnchorKey.PromptL1, snapshot.L1),
        };

        // UiView = 同 canonical + 头提示层（同一份 __prompt_snapshot，与 LlmView.system 一致）
        var ui = new List<ConversationMessage>(canonical)
        {
            SystemMsg(AnchorKey.PromptL0, snapshot.L0),
            SystemMsg(AnchorKey.SkillCombined, snapshot.SkillBlock),
            SystemMsg(AnchorKey.PromptL1, snapshot.L1),
        };

        var v = ContextBindingValidator.Validate(ui, llm, snapshot);
        Assert.AreEqual(0, v.Count, $"应为 UI≡LLM 双向逐字一致 + 零额外，违规: {string.Join(" | ", v)}");
    }

    [TestMethod]
    public void ContextBindingGuard_Strict_ThrowsOnViolation()
    {
        var ui = new List<ConversationMessage> { Msg(AnchorKey.History(1), "你好"), Msg(AnchorKey.Node("x"), "仅UI") };
        var llm = new List<ConversationMessage> { Msg(AnchorKey.History(1), "你好") };
        var threw = false;
        try
        {
            ContextBindingGuard.Check("test", ui, llm, null, strict: true);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Assert.IsTrue(threw, "strict 门禁 fail-fast：违规应抛 InvalidOperationException");
    }

    [TestMethod]
    public void ContextBindingGuard_NonStrict_WritesDiagnosticsOnly()
    {
        var ui = new List<ConversationMessage> { Msg(AnchorKey.History(1), "你好"), Msg(AnchorKey.Node("x"), "仅UI") };
        var llm = new List<ConversationMessage> { Msg(AnchorKey.History(1), "你好") };
        var seen = new System.Collections.Generic.List<BindingViolation>();
        var v = ContextBindingGuard.Check("test", ui, llm, null, strict: false, sink: seen.Add);
        Assert.IsTrue(v.Any(x => x.Rule == BindingRule.R1_UI_SubsetOf_LLM), "宽松模式返回违规");
        Assert.IsTrue(seen.Count == v.Count, "诊断 sink 收到全部违规");
    }

    [TestMethod]
    public void ConversationMessage_WithBindingTailParam_PreservesExistingPositional()
    {
        // 既有 6 参位置构造不破坏
        var plain = new ConversationMessage(ConversationRole.User, null, null, "hi", 1, default);
        Assert.IsNull(plain.Binding, "未传 Binding 尾参 → null（兼容既有调用）");

        // 带 Binding 尾参构造
        var binding = new MessageBinding(AnchorKey.History(1), MessageOrigin.LlmHistory, FeedTarget.History, PromptSnapshotBuilder.Hash("hi"));
        var bound = new ConversationMessage(ConversationRole.User, null, null, "hi", 1, default, null, binding);
        Assert.AreEqual(AnchorKey.History(1), bound.Binding!.Key);
        Assert.AreEqual(MessageOrigin.LlmHistory, bound.Binding.Origin);
    }

    // ---- P3：ContextBindingValidator（R1-R6） ----

    [TestMethod]
    public void Validator_IdenticalUiAndLlmHistory_NoViolations()
    {
        var ui = new List<ConversationMessage>
        {
            Msg(AnchorKey.History(1), "你好"),
            Msg(AnchorKey.Node("nodeA"), "产出A"),
        };
        var llm = new List<ConversationMessage>
        {
            Msg(AnchorKey.History(1), "你好"),
            Msg(AnchorKey.Node("nodeA"), "产出A"),
        };
        Assert.AreEqual(0, ContextBindingValidator.Validate(ui, llm, null).Count);
    }

    [TestMethod]
    public void Validator_UiHasExtraMessage_R1Fires()
    {
        var ui = new List<ConversationMessage> { Msg(AnchorKey.History(1), "你好"), Msg(AnchorKey.Node("x"), "仅展示") };
        var llm = new List<ConversationMessage> { Msg(AnchorKey.History(1), "你好") };
        var v = ContextBindingValidator.Validate(ui, llm, null);
        Assert.IsTrue(v.Any(x => x.Rule == BindingRule.R1_UI_SubsetOf_LLM), "UI 多余消息 → R1");
    }

    [TestMethod]
    public void Validator_LlmHasExtraMessage_R2Fires()
    {
        var ui = new List<ConversationMessage> { Msg(AnchorKey.History(1), "你好") };
        var llm = new List<ConversationMessage> { Msg(AnchorKey.History(1), "你好"), Msg(AnchorKey.Node("y"), "LLM 才有") };
        var v = ContextBindingValidator.Validate(ui, llm, null);
        Assert.IsTrue(v.Any(x => x.Rule == BindingRule.R2_LLM_SubsetOf_UI), "LLM 多余消息 → R2");
    }

    [TestMethod]
    public void Validator_UnboundUiMessage_R6Fires()
    {
        var ui = new List<ConversationMessage>
        {
            Msg(AnchorKey.History(1), "你好"),
            new ConversationMessage(ConversationRole.Tool, "nodeX", "nodeX", "无绑定额外消息", 1, default),
        };
        var llm = new List<ConversationMessage> { Msg(AnchorKey.History(1), "你好") };
        var v = ContextBindingValidator.Validate(ui, llm, null);
        Assert.IsTrue(v.Any(x => x.Rule == BindingRule.R6_ZeroExtra), "未绑定消息 → R6");
    }

    [TestMethod]
    public void Validator_DuplicateKey_R4Fires()
    {
        var ui = new List<ConversationMessage> { Msg(AnchorKey.History(1), "a"), Msg(AnchorKey.History(1), "b") };
        var v = ContextBindingValidator.Validate(ui, ui, null);
        Assert.IsTrue(v.Any(x => x.Rule == BindingRule.R4_KeyUnique), "重复 AnchorKey → R4");
    }

    [TestMethod]
    public void Validator_UnresolvableKey_R5Fires()
    {
        var ui = new List<ConversationMessage> { Msg(AnchorKey.From(""), "空键"), Msg(AnchorKey.From("history.abc"), "非法历史键") };
        var v = ContextBindingValidator.Validate(ui, Array.Empty<ConversationMessage>(), null);
        Assert.IsTrue(v.Count(x => x.Rule == BindingRule.R5_KeyResolvable) >= 2, "空键/非法 history → R5");
    }

    [TestMethod]
    public void Validator_SystemMismatch_R3Fires()
    {
        var snap = PromptSnapshotBuilder.Build("L0真实", "SK真实", "L1真实", "", new[] { "cv" });
        var ui = new List<ConversationMessage> { SystemMsg(AnchorKey.PromptL0, "L0真实") };
        // LLM system 段文本与快照不一致
        var llm = new List<ConversationMessage> { SystemMsg(AnchorKey.PromptL0, "L0被改") };
        var v = ContextBindingValidator.Validate(ui, llm, snap);
        Assert.IsTrue(v.Any(x => x.Rule == BindingRule.R3_SystemExact && x.Source == "Llm"), "LLM system 段与快照不一致 → R3");
    }

    [TestMethod]
    public void Validator_SystemAligned_NoR3Violation()
    {
        var snap = PromptSnapshotBuilder.Build("L0", "SK", "L1", "", new[] { "cv" });
        var ui = new List<ConversationMessage> { SystemMsg(AnchorKey.PromptL0, "L0"), SystemMsg(AnchorKey.PromptL1, "L1"), SystemMsg(AnchorKey.SkillCombined, "SK") };
        var llm = new List<ConversationMessage> { SystemMsg(AnchorKey.PromptL0, "L0"), SystemMsg(AnchorKey.PromptL1, "L1"), SystemMsg(AnchorKey.SkillCombined, "SK") };
        Assert.IsFalse(ContextBindingValidator.Validate(ui, llm, snap).Any(x => x.Rule == BindingRule.R3_SystemExact), "system 一致 → 无 R3");
    }

    private static ConversationMessage Msg(AnchorKey key, string text)
        => new(ConversationRole.User, null, null, text, 1, default,
            Binding: new MessageBinding(key, MessageOrigin.LlmHistory, FeedTarget.History, PromptSnapshotBuilder.Hash(text)));

    private static ConversationMessage SystemMsg(AnchorKey key, string text)
        => new(ConversationRole.System, null, null, text, 0, default,
            Binding: new MessageBinding(key, MessageOrigin.LlmSystem, FeedTarget.System, PromptSnapshotBuilder.Hash(text)));
}
