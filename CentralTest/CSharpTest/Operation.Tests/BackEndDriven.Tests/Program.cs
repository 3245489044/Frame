
namespace BackEndDriven.Tests;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var allPassed = true;
        var runnerTests = new BackEndPipelineRunnerTests();

        try
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  管线工作流调用委托路径测试");
            Console.WriteLine("========================================\n");
            runnerTests.RunAll_PipelineWorkflowInvocation();
            Console.WriteLine("\n  管线工作流调用测试通过！\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n  管线工作流调用测试失败: {ex.Message}\n");
            allPassed = false;
        }

        try
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  RecordCommandResult 行为验证");
            Console.WriteLine("========================================\n");
            runnerTests.RunAll_RecordCommandResult();
            Console.WriteLine("\n  RecordCommandResult 测试通过！\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n  RecordCommandResult 测试失败: {ex.Message}\n");
            allPassed = false;
        }

        Console.WriteLine("========================================");
        Console.WriteLine(allPassed ? "  所有测试通过！" : "  存在失败的测试！");
        Console.WriteLine("========================================");

        Environment.Exit(allPassed ? 0 : 1);
    }
}