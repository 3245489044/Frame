#nullable enable

namespace Modules.Maintain.Ports
{
    /// <summary>可提取片段的一个段：键 + 内容（内容为去缩进后的原始文本块，含键行与其子行）。</summary>
    public sealed record FragmentSegment(string Key, string Content);

    /// <summary>
    /// 片段抽取策略：把「哪些内容可提取为共享片段 / 如何合成片段 / 如何把数据项改写为引用形态」
    /// 这些领域规则从操作集分离出来。
    /// <para>
    /// <see cref="Operations.MaintainOperations{TData}.ExtractFragment"/> 只做编排
    /// （读段 → 合成 → 写片段项 → 改写各数据项），不绑定任何具体数据格式；
    /// 新增一种数据格式只需实现本端口，编排逻辑零改动。
    /// </para>
    /// </summary>
    public interface IFragmentStrategy<TData>
    {
        /// <summary>片段项在工作区中的键（唯一标识，如共享片段文件路径）。</summary>
        string FragmentKey { get; }

        /// <summary>读出数据项中可提取的段（应本地保留的段不返回）。</summary>
        IReadOnlyList<FragmentSegment> ReadSegments(TData item);

        /// <summary>把共享段合成为片段内容（注释 / 缩进等呈现规则由策略决定）。</summary>
        string ComposeFragment(IReadOnlyList<FragmentSegment> sharedSegments);

        /// <summary>由片段内容构造片段项（写入工作区）。</summary>
        TData CreateFragmentItem(string content);

        /// <summary>数据项应写入的片段引用标识（如相对路径）。</summary>
        string ReferenceFor(TData item);

        /// <summary>把数据项改写为「本地段 + 引用片段」形态。</summary>
        TData RewriteAsReference(TData item, string reference);
    }
}
