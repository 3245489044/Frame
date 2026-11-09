using System.Globalization;
using Share.Conversation.ContextBinding;

namespace Share.Conversation.View;

/// <summary>
/// 帧身份判别式（**单源**）：去重键与发布行键共用同一判别式，禁两处各写一套。
/// <para><c>Seq &gt; 0</c> ⇒ 到达序；<c>Seq == 0</c>（缺省，不代表同一帧）⇒ 文本哈希。</para>
/// </summary>
internal static class FrameIdentity
{
    /// <summary>域内判别式：到达序或文本哈希（哈希复用 <see cref="PromptSnapshotBuilder.Hash"/>，不新增算法）。</summary>
    /// <param name="frame">读出的帧。</param>
    public static string Discriminator(ViewFrame frame)
        => frame.Seq > 0
            ? frame.Seq.ToString(CultureInfo.InvariantCulture)
            : "t" + PromptSnapshotBuilder.Hash(frame.Text);

    /// <summary>跨源合并键（不含域：保持跨源合并语义）。</summary>
    /// <param name="frame">读出的帧。</param>
    public static string MergeKey(ViewFrame frame)
        => $"{frame.Kind}\u001F{frame.Round}\u001F{Discriminator(frame)}";

    /// <summary>发布行键（含域：行在视图内唯一；<c>Seq == 0</c> 不得退化为同一键）。</summary>
    /// <param name="domain">读源域键。</param>
    /// <param name="frame">读出的帧。</param>
    public static string RowKey(string domain, ViewFrame frame)
        => $"{domain}:{frame.Round}:{Discriminator(frame)}:{frame.Kind}";
}
