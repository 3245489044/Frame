using TConfig;
using UniversalMiddleware;

namespace FrontEndDriven.Shared;

public static class EventHelper
{

    public const string EventsFieldName = "events";

    public static readonly string[] CommonOptionalFieldsWithEvents =
        { "layout", "style", EventsFieldName, "toolTip", "enabled", "visible" };


    private static readonly (string LowerName, string PascalName)[] StandardEventEntries =
    {
        ("click", "Click"),
        ("doubleclick", "DoubleClick"),
        ("textchanged", "TextChanged"),
        ("selectedindexchanged", "SelectedIndexChanged"),
        ("selectedvaluechanged", "SelectedValueChanged"),
        ("checkedchanged", "CheckedChanged"),
        ("valuechanged", "ValueChanged"),
        ("enter", "Enter"),
        ("leave", "Leave"),
        ("gotfocus", "GotFocus"),
        ("lostfocus", "LostFocus"),
        ("keydown", "KeyDown"),
        ("keypress", "KeyPress"),
        ("keyup", "KeyUp"),
        ("mousedown", "MouseDown"),
        ("mouseenter", "MouseEnter"),
        ("mouseleave", "MouseLeave"),
        ("mousemove", "MouseMove"),
        ("mouseup", "MouseUp"),
        ("mouseclick", "MouseClick"),
        ("mousedoubleclick", "MouseDoubleClick"),
        ("mousehover", "MouseHover"),
        ("mousewheel", "MouseWheel"),
        ("resize", "Resize"),
        ("sizechanged", "SizeChanged"),
        ("paint", "Paint"),
        ("visiblechanged", "VisibleChanged"),
        ("enabledchanged", "EnabledChanged"),
        ("dragdrop", "DragDrop"),
        ("dragenter", "DragEnter"),
        ("dragleave", "DragLeave"),
        ("dragover", "DragOver"),
        ("scroll", "Scroll"),
        ("validating", "Validating"),
        ("validated", "Validated"),
        ("itemclick", "ItemClick"),
        ("itemselectionchanged", "ItemSelectionChanged"),
        ("afterselect", "AfterSelect"),
        ("selectionchanged", "SelectionChanged"),
        ("message", "MessageReceived"),
        ("cellclick", "CellClick"),
        ("cellcontentclick", "CellContentClick"),
        ("rowenter", "RowEnter"),
        ("rowleave", "RowLeave"),
        ("splittermoved", "SplitterMoved"),
        ("formclosing", "FormClosing"),
        ("formclosed", "FormClosed"),
        ("load", "Load"),
        ("shown", "Shown"),
        ("activated", "Activated"),
        ("deactivate", "Deactivate"),
        ("helprequested", "HelpRequested"),
    };

    public static readonly HashSet<string> StandardEventTypes = new(
        StandardEventEntries.Select(e => e.LowerName),
        StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> EventPascalMap = new(
        StandardEventEntries.ToDictionary(e => e.LowerName, e => e.PascalName),
        StringComparer.OrdinalIgnoreCase);

    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return EventPascalMap.TryGetValue(name, out var mapped) ? mapped : char.ToUpper(name[0]) + name[1..];
    }


    public static Dictionary<string, object> BuildEvent(
        Dictionary<string, object>? events,
        string eventName,
        string functionName)
    {
        events ??= new Dictionary<string, object>();
        events[eventName] = functionName;
        return events;
    }


    public static bool IsValidEventType(string? eventType)
    {
        return !string.IsNullOrEmpty(eventType) && StandardEventTypes.Contains(eventType);
    }

    public static bool HasEvents(ControlConfig config)
    {
        return config?.Events != null;
    }

    private static UniversalMiddleware.EventMapping.IEventMappingService MappingService =>
        UniversalMiddlewareEntry.Instance.EventMapping;

    public static List<string>? GetEventFunctions(EventConfig events, string eventType)
    {
        if (events == null) return null;
        return MappingService.GetEventFunctions(events, eventType);
    }
}