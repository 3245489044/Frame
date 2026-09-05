using RoslynSpace.RoslynSyntaxUtilities.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Analysis
{
    public static class CompilationSuggestionService
    {
        public static List<CompilationSuggestion> GenerateSuggestions(List<CompilationError> errors)
        {
            if (errors == null || !errors.Any())
                return new List<CompilationSuggestion>();

            return errors
                .Select(e => GetSuggestionForError(e))
                .Distinct()
                .ToList();
        }

        public static CompilationSuggestion GetSuggestionForError(CompilationError error)
        {
            if (error == null)
                return null;

            return error.Id switch
            {
                "CS0246" => new CompilationSuggestion("CS0246", "缺少类型或命名空间引用",
                    new[] { "检查是否缺少 using 指令", "确认程序集引用是否正确", "检查类型名称拼写" }),
                "CS0103" => new CompilationSuggestion("CS0103", "名称在当前上下文中不存在",
                    new[] { "检查变量或方法名称拼写", "确认变量是否在正确的作用域内声明", "检查访问修饰符是否正确" }),
                "CS1002" => new CompilationSuggestion("CS1002", "缺少分号",
                    new[] { "在语句末尾添加分号 ;", "检查语句是否完整" }),
                "CS0116" => new CompilationSuggestion("CS0116", "命名空间直接包含成员定义",
                    new[] { "将成员定义放在类或结构体内", "检查大括号是否正确闭合" }),
                "CS1513" => new CompilationSuggestion("CS1513", "缺少右大括号",
                    new[] { "在代码块末尾添加 }", "检查大括号是否匹配" }),
                "CS1525" => new CompilationSuggestion("CS1525", "无效的表达式术语",
                    new[] { "检查语法是否正确", "确认运算符和操作数是否完整" }),
                "CS1026" => new CompilationSuggestion("CS1026", "缺少 )",
                    new[] { "在表达式末尾添加 )", "检查括号是否匹配" }),
                "CS1001" => new CompilationSuggestion("CS1001", "需要标识符",
                    new[] { "检查变量或类型名称是否缺失", "确认声明语法是否正确" }),
                "CS0117" => new CompilationSuggestion("CS0117", "类型不包含指定成员",
                    new[] { "检查成员名称拼写", "确认类型是否包含该成员" }),
                "CS1061" => new CompilationSuggestion("CS1061", "类型不包含指定成员且无扩展方法",
                    new[] { "检查方法或属性名称拼写", "确认是否缺少 using 指令（扩展方法需要）" }),
                "CS1503" => new CompilationSuggestion("CS1503", "参数类型不匹配",
                    new[] { "检查参数类型是否正确", "确认参数顺序是否正确" }),
                "CS0029" => new CompilationSuggestion("CS0029", "无法隐式转换类型",
                    new[] { "使用显式转换", "检查类型是否兼容" }),
                "CS0165" => new CompilationSuggestion("CS0165", "使用了未赋值的局部变量",
                    new[] { "在使用变量前为其赋值", "检查变量初始化" }),
                "CS0200" => new CompilationSuggestion("CS0200", "属性或索引器只读",
                    new[] { "检查属性是否有 set 访问器", "确认是否使用了错误的属性" }),
                _ => new CompilationSuggestion(error.Id, "请查看详细错误信息",
                    new[] { "查阅相关文档", "检查语法规范" })
            };
        }
    }
}
