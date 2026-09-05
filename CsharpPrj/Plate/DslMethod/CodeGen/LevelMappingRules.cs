using Plate.DslMethod.Abstractions;

namespace Plate.DslMethod.CodeGen;

/// <summary>
/// 反转生成层级映射规则（文档 §7）。映射以**节标题为主、文件缺省为辅**逐声明确定：
///  - % 配置参数 → L3；
///  - 节标题关键字优先（logic-axioms 的 B1/B2→L0、B3→L0.5、B4→L1、B5→L2、B6→L2、B7→L3、A0-A3→L1）；
///  - 文件缺省（system.prompt.dsl→L0、loop-axioms→L1、l1l2-generator→L0.5、boundaries→L2）。
/// </summary>
public static class LevelMappingRules
{
    /// <summary>依文件 + 节标题判定公理层级（% 配置参数已在符号过滤层排除，不落入本规则）。</summary>
    public static AxiomLevel Resolve(string file, string sectionTitle, string prefix)
    {
        // 节标题优先
        if (!string.IsNullOrWhiteSpace(sectionTitle))
        {
            var s = sectionTitle.ToLowerInvariant();
            if (s.Contains("b7") || s.Contains("l3")) return AxiomLevel.L3;
            if (s.Contains("b6") || s.Contains("元级") || s.Contains("strategy")) return AxiomLevel.L2;
            if (s.Contains("b5") || s.Contains("领域公理")) return AxiomLevel.L2;
            if (s.Contains("b4") || s.Contains("l1 frame")) return AxiomLevel.L1;
            if (s.Contains("b3") || s.Contains("l0.5")) return AxiomLevel.L0_5;
            if (s.Contains("a0") || s.Contains("a1") || s.Contains("a2") || s.Contains("a3")
                || s.Contains("逻辑算子") || s.Contains("端口组合") || s.Contains("标准流水线")) return AxiomLevel.L1;
            if (s.Contains("b1") || s.Contains("b2") || s.Contains("l0 core") || s.Contains("l0 bax")) return AxiomLevel.L0;
            if (s.Contains("b0") || s.Contains("层级结构")) return AxiomLevel.L0;
        }

        // 文件缺省（契约源统一为 DSL 承载技能：skills/{l0,l0,l0,rare}/.../SKILL.md）
        var f = file.ToLowerInvariant();
        if (f.Contains("l0-system")) return AxiomLevel.L0;
        if (f.Contains("l0-loop")) return AxiomLevel.L1;
        if (f.Contains("l1l2")) return AxiomLevel.L0_5;
        if (f.Contains("boundaries")) return AxiomLevel.L2;
        if (f.Contains("l0-format")) return AxiomLevel.L2; // 格式段契约默认 L2
        if (f.Contains("l0-logic")) return AxiomLevel.L2;  // 兜底：Logic 契约未被节标题命中时落 L2

        return AxiomLevel.L2;
    }
}
