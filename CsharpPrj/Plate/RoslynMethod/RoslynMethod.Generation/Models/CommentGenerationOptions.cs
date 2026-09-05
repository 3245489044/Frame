namespace RoslynSpace.RoslynSyntaxUtilities.Generation
{
    public enum CommentStyle
    {
        SingleLine,
        MultiLine,
        Documentation
    }

    public class CommentGenerationOptions
    {
        public CommentStyle DefaultStyle { get; set; } = CommentStyle.SingleLine;

        public bool SkipIfExistingComment { get; set; } = true;
    }
}
