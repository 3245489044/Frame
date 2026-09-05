
using Modules.EventFramework.Services;
using Modules.Pipeline;

namespace WorkFlowMethod.Output.Stages;

public class OutputWriteStage : OutputStageBase
{
    private readonly EnhancedValueWriter _writer;

    public override string Name => "OutputWrite";

    public override int Order => 10;

    public override bool IsFatal => true;

    public OutputWriteStage(EnhancedValueWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    protected override object? ExecuteCore(OutputWriteContext ctx, ErrorCollector errors, PipelineContext context)
    {
        if (ctx.Sender == null)
        {
            errors.Add("OUTPUT_WRITE_SENDER_NULL",
                "事件触发控件（Sender）为 null，无法写入",
                ErrorLevel.Fatal);
            return null;
        }

        try
        {
            var success = _writer.Write(ctx.Sender, ctx.OutputTarget, ctx.RawValue);
            ctx.WriteSuccess = success;

            if (!success)
            {
                errors.Add("OUTPUT_WRITE_FAILED",
                    "EnhancedValueWriter 写入失败",
                    ErrorLevel.Error);
            }
        }
        catch (Exception ex)
        {
            ctx.WriteSuccess = false;
            ctx.ErrorMessage = ex.Message;

            errors.Add("OUTPUT_WRITE_EXCEPTION",
                $"写入异常: {ex.Message}",
                ErrorLevel.Error,
                ex);
        }

        return ctx;
    }
}
