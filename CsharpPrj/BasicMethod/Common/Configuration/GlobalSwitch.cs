namespace Common.GraphMindStudio
{
    public static class GlobalSwitch
    {
        public static bool localswitchbool = true;
        public static bool headlessbool = true;

        public static bool useCustomPointer = true;
        public static bool UseUIZbool = true;

        public static bool StartEdge = false;
        public static string StorageKey = "SequentialStorageTable";

        public static bool UseGraphWorkflowUpdate = false;
    }

    public static class UIPaths
    {
        public const string Rebuild = "Rebuild";
        public const string ShowParameterValue = "Canvas/MenuCanvas/MainGroup/ShowParameterValue";
        public const string WebValue = "Canvas/MenuCanvas/MainGroup/WebValue";
        public const string DyanmicParameterWiondw = "Canvas/INPUTCanvas";
        public const string MainCanvas = "Canvas";

        public const string WebComposite = "Canvas/MenuCanvas/MainGroup/WebComposite";
        public const string Content = "Viewport/Content";
        public const string RectBase = "Viewport/Content/RectBase";
        public const string ContextMenu = "Canvas/INPUTCanvas/Scroll View(Clone)/Viewport/Content/RightMenu/ContextMenu(Clone)";
        public const string RightMenu = "Canvas/INPUTCanvas/Scroll View(Clone)/Viewport/Content/RightMenu";
        public const string Prefabs = "Prefabs/UIPrefebs/";
    }

}
