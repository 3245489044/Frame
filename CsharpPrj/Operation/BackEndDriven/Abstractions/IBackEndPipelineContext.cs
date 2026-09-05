
using BackEndDriven.Models;
using CommandLineResult = TConfig.CommandLineResult;
using Share.TaskStatusPool.Abstractions;

namespace BackEndDriven.Abstractions;

public interface IBackEndPipelineContext : IPipelineContext
{
    List<CommandLineResult> CommandResults { get; }

    void RecordCommandResult(CommandLineResult result);
}


