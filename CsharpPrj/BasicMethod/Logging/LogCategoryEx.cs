
namespace Common.Diagnostics
{
    public enum LogCategoryEx : ulong
    {
        WdUI_Render       = 0x0001_0000,
        WdUI_Layout       = 0x0002_0000,
        WdUI_Container    = 0x0004_0000,
        WdUI_Resize       = 0x0008_0000,
        WdUI_Strategy     = 0x0010_0000,
        WdUI_Template     = 0x0020_0000,
        WdUI_Pipeline     = 0x0040_0000,
        WdUI_Diagnostic   = 0x0080_0000,
        WdUI_All          = 0x00FF_0000,

        FileDriven_General    = 0x0100_0000,
        FileDriven_Command    = 0x0200_0000,
        FileDriven_Inspector  = 0x0400_0000,
        FileDriven_Diagnostic = 0x0800_0000,
        FileDriven_All        = 0x0F00_0000,

        Launcher_Bootstrap = 0x0001_0000_0000,
        Launcher_Init      = 0x0002_0000_0000,
        Launcher_Lifecycle = 0x0004_0000_0000,
        Launcher_All       = 0x0007_0000_0000,

        FunctionStorage_General  = 0x0000_0100_0000_0000,
        FunctionStorage_Workflow = 0x0000_0200_0000_0000,
        FunctionStorage_IO       = 0x0000_0400_0000_0000,
        FunctionStorage_UI       = 0x0000_0800_0000_0000,
        FunctionStorage_All      = 0x0000_0F00_0000_0000,

        Batch_Lifecycle = 0x0001_0000_0000_0000,
        Batch_Routing   = 0x0002_0000_0000_0000,
        Batch_Query     = 0x0004_0000_0000_0000,
        Batch_Export    = 0x0008_0000_0000_0000,
        Batch_All       = 0x000F_0000_0000_0000,

        WorkFlowMethod_Execution = 0x0100_0000_0000_0000,
        WorkFlowMethod_Registry  = 0x0200_0000_0000_0000,
        WorkFlowMethod_All       = 0x0300_0000_0000_0000,

    }

}
