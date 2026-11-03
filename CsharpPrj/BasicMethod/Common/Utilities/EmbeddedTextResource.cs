using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Modules.Common.Utilities
{
    /// <summary>
    /// 嵌入资源读取**通用框架**：按资源名后缀定位 + 读文本 + 缺失可见化。
    /// <para>
    /// 存在理由（SingleImplementation / TH-12）：按后缀找资源 + 开流 + StreamReader 的逐字同构实现
    /// 曾在 Agent / Frame 多处各写一遍，异常文案与 required 语义各不相同；本件为唯一实现。
    /// </para>
    /// <para>
    /// 纪律：① 缺失 = 显式失败（可带 <c>subject</c> 业务名，禁静默空标准）；② 同后缀多命中取首个
    /// （调用方须保证后缀互不包含）；③ 只读资源，不落盘、不缓存（Lazy 由消费方按需自持）。
    /// </para>
    /// </summary>
    public static class EmbeddedTextResource
    {
        /// <summary>资源缺失结论码（诊断可见化用；禁以异常文本外观表达成败）。</summary>
        public const string MissingCode = "resource.missing";

        /// <summary>按后缀定位资源名（未命中 ⇒ null）。</summary>
        public static string? FindName(Assembly assembly, string suffix)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentException.ThrowIfNullOrWhiteSpace(suffix);
            return assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>资源是否存在（裁剪/自带宿主场景的诊断可见性）。</summary>
        public static bool Exists(Assembly assembly, string suffix)
            => FindName(assembly, suffix) is not null;

        /// <summary>读取资源文本；缺失即抛（fail-fast）。<paramref name="subject"/> = 业务名（进异常文案）。</summary>
        public static string Read(Assembly assembly, string suffix, string? subject = null)
            => ReadOrNull(assembly, suffix, subject)
               ?? throw new InvalidOperationException(MissingMessage(suffix, subject));

        /// <summary>读取资源文本；缺失 ⇒ null（调用方可自定降级，但须显式可见）。</summary>
        public static string? ReadOrNull(Assembly assembly, string suffix, string? subject = null)
        {
            var name = FindName(assembly, suffix);
            if (name is null) return null;

            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"无法打开嵌入资源 {name}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>缺失文案（单源；<paramref name="subject"/> 为空时省略括号段）。</summary>
        public static string MissingMessage(string suffix, string? subject = null)
            => string.IsNullOrWhiteSpace(subject)
                ? $"程序集缺少嵌入资源 {suffix}"
                : $"程序集缺少嵌入资源 {suffix}（{subject}）";
    }
}
