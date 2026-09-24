namespace WorkFlowMethod.Skeletons.NodeProcess.KeyMapping
{
    /// <summary>
    /// 层次化键格式<b>声明</b>（U10：声明化 + 兼容默认；治 P14）：
    /// 前缀/后缀/分隔符/数字格式一律来自本声明，运行期只读——不使用 AsyncLocal 等可变进程静态配置，
    /// 多工作流并发不串味。缺省值 = 历史行为（兼容默认）。
    /// </summary>
    /// <param name="NumberFormat">数字格式（空 = 十进制无填充；"D3" = 三位零填充；其它 = 标准数字格式串）。</param>
    /// <param name="Prefix">层次前缀。</param>
    /// <param name="Suffix">层次后缀。</param>
    /// <param name="Separator">前缀/后缀与层次的分隔符（空 = 无分隔）。</param>
    public sealed record KeyHierarchyFormat(
        string NumberFormat = "",
        string Prefix = "_",
        string Suffix = "_",
        string Separator = "")
    {
        /// <summary>兼容默认（与历史缺省逐项一致：前缀/后缀 "_"、无分隔、十进制无填充）。</summary>
        public static KeyHierarchyFormat CompatibleDefault { get; } = new();
    }
}
