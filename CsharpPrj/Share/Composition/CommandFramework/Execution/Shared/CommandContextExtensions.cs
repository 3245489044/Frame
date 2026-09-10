

using CommandFramework.Abstractions;
using CommandFramework.Models;
using CommandFramework.Parsing;

namespace CommandFramework.Abstractions
{
    public static class CommandContextExtensions
    {
        private const string KeyFunctionExecuteSuccess = "__CommandCtx_FunctionExecuteSuccess";
        private const string KeyFunctionErrorMessage = "__CommandCtx_FunctionErrorMessage";


        public static IParsedCommand? GetParsedCommand(this TConfig.IExecutionContext ctx)
        {
            if (ctx is CommandContext cmdCtx)
                return cmdCtx.ParsedCommand;
            return null;
        }

        public static void SetParsedCommand(this TConfig.IExecutionContext ctx, IParsedCommand? value)
        {
            if (ctx is CommandContext cmdCtx)
                cmdCtx.ParsedCommand = value as ParsedCommand;
        }


        public static bool GetFunctionExecuteSuccess(this TConfig.IExecutionContext ctx)
        {
            if (ctx.Items.TryGetValue(KeyFunctionExecuteSuccess, out var value) && value is bool b)
                return b;
            return false;
        }

        public static void SetFunctionExecuteSuccess(this TConfig.IExecutionContext ctx, bool value)
        {
            ctx.Items[KeyFunctionExecuteSuccess] = value;
        }


        public static string? GetFunctionErrorMessage(this TConfig.IExecutionContext ctx)
        {
            if (ctx.Items.TryGetValue(KeyFunctionErrorMessage, out var value) && value is string s)
                return s;
            return null;
        }

        public static void SetFunctionErrorMessage(this TConfig.IExecutionContext ctx, string? value)
        {
            if (value != null)
                ctx.Items[KeyFunctionErrorMessage] = value;
            else
                ctx.Items.Remove(KeyFunctionErrorMessage);
        }


        public static object? GetFunctionResultValue(this TConfig.IExecutionContext ctx)
        {
            if (ctx is CommandContext cmdCtx)
                return cmdCtx.FunctionResultValue;
            return null;
        }

        public static void SetFunctionResultValue(this TConfig.IExecutionContext ctx, object? value)
        {
            if (ctx is CommandContext cmdCtx)
                cmdCtx.FunctionResultValue = value;
        }


        public static string? GetBoundFunctionName(this TConfig.IExecutionContext ctx)
        {
            if (ctx is CommandContext cmdCtx)
                return cmdCtx.BoundFunctionName;
            return null;
        }

        public static void SetBoundFunctionName(this TConfig.IExecutionContext ctx, string? value)
        {
            if (ctx is CommandContext cmdCtx)
                cmdCtx.BoundFunctionName = value;
        }


        public static string? GetCurrentCommand(this TConfig.IExecutionContext ctx)
        {
            if (ctx is CommandContext cmdCtx)
                return cmdCtx.CurrentCommand;
            return null;
        }

        public static void SetCurrentCommand(this TConfig.IExecutionContext ctx, string? value)
        {
            if (ctx is CommandContext cmdCtx)
                cmdCtx.CurrentCommand = value;
        }


        public static void ResetCommandContext(this TConfig.IExecutionContext ctx)
        {
            if (ctx is CommandContext cmdCtx)
            {
                cmdCtx.Reset();
                return;
            }
            ctx.Items.Remove(KeyFunctionExecuteSuccess);
            ctx.Items.Remove(KeyFunctionErrorMessage);
        }
    }
}
