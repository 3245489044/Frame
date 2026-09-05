using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Conversation.Rendering;

namespace Share.Conversation.Tests;

[TestClass]
public class SkillHistoryMappingTests
{
    [TestMethod]
    public void KeyFor_ProducesStableHistoryKeys()
    {
        Assert.AreEqual("skill.analyze-folder.block", SkillHistoryMapping.KeyForBlock("analyze-folder"));
        Assert.AreEqual("skill.analyze-folder.end", SkillHistoryMapping.KeyForEnd("analyze-folder"));
        Assert.AreEqual("wf.agent-round.skill.analyze-folder", SkillHistoryMapping.KeyForWorkflowNode("agent-round", "analyze-folder"));
    }

    [TestMethod]
    public void ResolveMappings_EachSkill_YieldsBlockAndEnd()
    {
        var mappings = SkillHistoryMapper.ResolveMappings("task-a", 3, new[] { "a", "b" });

        Assert.AreEqual(4, mappings.Count);
        Assert.IsTrue(mappings.All(m => m.TaskKey == "task-a" && m.Round == 3));
        Assert.AreEqual(1, mappings.Count(m => m.SkillName == "a" && m.Position == SkillHistoryPosition.HeadBlock));
        Assert.AreEqual(1, mappings.Count(m => m.SkillName == "a" && m.Position == SkillHistoryPosition.EndNode));
        Assert.AreEqual(1, mappings.Count(m => m.SkillName == "b" && m.Position == SkillHistoryPosition.HeadBlock));
        Assert.AreEqual(1, mappings.Count(m => m.SkillName == "b" && m.Position == SkillHistoryPosition.EndNode));
        Assert.AreEqual("skill.a.block", mappings.Single(m => m.SkillName == "a" && m.Position == SkillHistoryPosition.HeadBlock).HistoryKey);
        Assert.AreEqual("skill.b.end", mappings.Single(m => m.SkillName == "b" && m.Position == SkillHistoryPosition.EndNode).HistoryKey);
    }

    [TestMethod]
    public void ResolveMappings_Deduplicates_CaseInsensitive()
    {
        var mappings = SkillHistoryMapper.ResolveMappings("t", 0, new[] { "A", "a" });

        // 大小写不敏感去重：同一技能只保留一个技能名（产生块 + 末尾两行），保留首现名 "A"。
        Assert.AreEqual(2, mappings.Count);
        Assert.AreEqual(1, mappings.Select(m => m.SkillName).Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
        Assert.AreEqual("A", mappings[0].SkillName);
    }

    [TestMethod]
    public void ResolveMappings_EmptyOrNull_ReturnsEmpty()
    {
        Assert.AreEqual(0, SkillHistoryMapper.ResolveMappings("t", 0, Array.Empty<string>()).Count);
        Assert.AreEqual(0, SkillHistoryMapper.ResolveMappings("t", 0, null!).Count);
    }
}
