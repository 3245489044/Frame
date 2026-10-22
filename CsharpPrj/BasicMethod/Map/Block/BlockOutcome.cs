#nullable enable

namespace BasicMethod.Block
{
    /// <summary>单块执行结果（由 <see cref="BlockRunner"/> 分类写入）。</summary>
    public enum BlockOutcome
    {
        Success,
        Skipped,
        Failed,
        Aborted
    }

    /// <summary>整条管线结果（部分成功与失败可区分）。</summary>
    public enum BlockRunOutcome
    {
        Success,
        PartialSuccess,
        Failed,
        Aborted
    }
}
