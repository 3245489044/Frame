namespace Common.GraphMindStudio
{
    public static class PROMPET
    {
        public static class GenerationofSpecialFuncitons
        {
            public static string Cshapr函数限制生成提示词 = @"
根据提供的逻辑描述，生成一段 C# 代码。要求如下：
只返回单个 Markdown 代码块，其中包含完整的代码。不要在代码块之外添加任何解释、说明或介绍文字。
代码应清晰简洁，逻辑清晰，无冗余。
包含必要的声明结构（如导入语句、命名空间、模块、类、主函数等），确保代码能直接编译/解释执行或作为独立片段粘贴使用。
在关键逻辑处添加注释，解释代码意图。
遵循C#的编码规范和最佳实践。
请直接输出代码块。
";
            public static string 示例函数生成提示词 = @"
根据提供的代码，编写一个演示类，展示其核心功能的使用方法。要求如下：
类结构：定义一个公开类，类名自拟。
核心方法：类中包含一个核心的演示方法，方法签名返回 string，方法名自拟。
数据准备：在核心方法内部创建有意义的测试数据，建议采用纯数学 / 逻辑方向（如整数数组）以保持通用性。
功能调用：使用给定代码中现有的类和方法构建并执行，充分展示其主要功能，不重新实现任何内部逻辑。
结果收集：使用 StringBuilder 收集输出结果，最后返回其内容。
集合解析：解析集合或复杂数据时，调用已有的 PrintCollection 方法解析集合，不得自行实现 PrintCollection 方法。
假设方法签名如下：
public static string PrintCollection(object collection, string separator = "", bool reflection = false)
常量定义：所有字符串和数字常量（如标题、操作名、消息模板、计数、阈值等）在核心方法内部开头定义为 const。
输出集中：先完成所有操作并保存结果，然后在核心方法末尾一次性通过 sb.AppendLine 添加所有输出（使用常量和结果变量）。
代码清晰：按功能模块组织逻辑，确保代码简洁、规范、功能完整。
生成的代码应具备良好的可读性和维护性，完全遵循上述原则。
";

            public static string 函数整合指令 = @"
请重构并优化提供的C#代码。具体要求如下：
保持原有功能
确保代码的行为不变，包括输入、输出、异常处理等所有功能细节。
修复潜在错误
仔细检查代码逻辑，修正任何可能存在的逻辑错误或漏洞。
简化与可维护性
在保证功能正确的前提下，简化代码结构，提高可读性和可维护性，但避免过度优化（如引入不必要的设计模式或复杂抽象）。
返回完整可编译代码
提供最简化的完整C#代码，包含所有必要的类定义，确保可以直接编译运行。
添加特性标记
对于代码中的示例函数（例如演示用途的方法），使用 [DynamicMethod] 特性进行标记。
引入必需的命名空间
分析代码逻辑，添加所有必要的 using 指令以确保函数编译正确。必须固定添加以下两个 using：
using DynamicMethodLookup;
using static GraphMindStudio.FuncCallFrame.Printer;
清理无效占位代码
移除当前代码中所有逻辑上无效或仅为占位用途的函数。
";
        }
    }
}
