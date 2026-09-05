# 使用 Add-Type 编译 STA 线程的 WinForms 启动器
Add-Type -AssemblyName System.Windows.Forms

$code = @'
using System;
using System.Windows.Forms;
using System.Runtime.Versioning;
using WdUI.UI;

[assembly: SupportedOSPlatform("windows")]

namespace WdUITestRunner
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!WdUIComposer.IsInitialized)
                WdUIComposer.Initialize();

            Application.Run(new WdUIComposerTestForm());
        }
    }
}
'@

$refs = @(
    "System.Windows.Forms.dll",
    "System.Drawing.Common.dll",
    "System.Runtime.dll",
    "System.ComponentModel.Primitives.dll",
    "d:\Project\Program\Workflow_\CentralConsole\bin\Debug\net10.0-windows\WdUI.dll",
    "d:\Project\Program\Workflow_\CentralConsole\bin\Debug\net10.0-windows\WdUI.Abstractions.dll",
    "d:\Project\Program\Workflow_\CentralConsole\bin\Debug\net10.0-windows\WdUI.Core.dll"
)

try {
    Add-Type -TypeDefinition $code -ReferencedAssemblies $refs -ErrorAction Stop
    [WdUITestRunner.Program]::Main()
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host "Fallback: 直接启动CentralConsole..." -ForegroundColor Yellow
    Start-Process -WindowStyle Normal -FilePath "dotnet" -ArgumentList "run --project d:\Project\Program\Workflow_\CentralConsole\CentralConsole.csproj -- wdui-test"
}
