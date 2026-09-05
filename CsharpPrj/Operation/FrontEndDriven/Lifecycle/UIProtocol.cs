
namespace FrontEndDriven.Protocol;

public static class UIProtocol
{

    public const string NodeType_Control = "ui.control";

    public const string NodeType_Container = "ui.container";

    public const string NodeType_Window = "ui.window";

    public const string NodeType_SplitContainer = "ui.splitcontainer";


    public const string EdgeType_Contains = "ui.contains";

    public const string EdgeType_Event = "ui.event";

    public const string EdgeType_Value = "ui.value";


    public const string Prop_ControlType = "controlType";

    public const string Prop_Name = "name";


    public const string Prop_Left = "left";

    public const string Prop_Top = "top";

    public const string Prop_Width = "width";

    public const string Prop_Height = "height";


    public const string Prop_FontFamily = "fontFamily";

    public const string Prop_FontSize = "fontSize";

    public const string Prop_FontBold = "fontBold";

    public const string Prop_FontItalic = "fontItalic";

    public const string Prop_FontUnderline = "fontUnderline";

    public const string Prop_ForeColor = "foreColor";

    public const string Prop_BackColor = "backColor";


    public const string Prop_Text = "text";

    public const string Prop_Enabled = "enabled";

    public const string Prop_Visible = "visible";

    public const string Prop_Value = "value";


    public const string Prop_SplitterDistance = "splitterDistance";

    public const string Prop_Orientation = "orientation";


    public const string Prop_EventName = "eventName";

    public const string Prop_FunctionName = "functionName";

    public const string Prop_BorderWidth = "borderWidth";

    public const string Prop_BorderColor = "borderColor";
    public const string Prop_Dock = "dock";
    public const string Prop_Anchor = "anchor";


    public const string SplitterPanel1 = "Panel1";

    public const string SplitterPanel2 = "Panel2";

    public const string Prop_EventBindings = "_eventBindings";

    public const string Prop_FunctionNames = "_functionNames";


    public const string Meta_EntityId = "entityId";

    public const string Meta_Timestamp = "timestamp";

    public const string Meta_PipelineStage = "pipelineStage";
}
