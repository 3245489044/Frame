using System.Globalization;
using System.Windows.Forms;
using TConfig.Controls;

namespace WinFormsValueContract;

internal static partial class BuiltInValueContracts
{
    // ── Checked / Range / Path 语义 ──

    private static void RegisterStateContracts(IDictionary<string, ControlValueContract> target)
    {
        RegisterCheckedContract(target, "checkbox");
        RegisterCheckedContract(target, "radiobutton");

        RegisterRangeContract(target, "numericupdown", nameof(NumericUpDown.ValueChanged));
        RegisterRangeContract(target, "trackbar", nameof(TrackBar.ValueChanged));
        RegisterRangeContract(target, "progressbar", null);

        target["picturebox"] = Contract(
            "picturebox",
            ValueSemantic.Path,
            new Dictionary<ValueSemantic, ValueAccessor>
            {
                [ValueSemantic.Path] = new(
                    c => ((PictureBox)c).ImageLocation,
                    (c, v) => { ((PictureBox)c).ImageLocation = v?.ToString(); return v?.ToString(); })
            });
    }

    private static void RegisterCheckedContract(IDictionary<string, ControlValueContract> target, string controlType)
    {
        target[controlType] = Contract(
            controlType,
            ValueSemantic.Checked,
            new Dictionary<ValueSemantic, ValueAccessor>
            {
                [ValueSemantic.Checked] = new(
                    c => ((CheckBox)c).Checked,
                    (c, v) =>
                    {
                        if (bool.TryParse(v?.ToString(), out var b))
                        {
                            ((CheckBox)c).Checked = b;
                            return b.ToString();
                        }
                        return null;
                    })
            },
            new Dictionary<ValueSemantic, string> { [ValueSemantic.Checked] = nameof(CheckBox.CheckedChanged) });
    }

    private static void RegisterRangeContract(IDictionary<string, ControlValueContract> target, string controlType, string? eventName)
    {
        target[controlType] = Contract(
            controlType,
            ValueSemantic.Range,
            new Dictionary<ValueSemantic, ValueAccessor>
            {
                [ValueSemantic.Range] = ValueAccessor.Create<Control>(ReadRange, WriteRange)
            },
            eventName is null ? null : new Dictionary<ValueSemantic, string> { [ValueSemantic.Range] = eventName });
    }

    private static object? ReadRange(Control c) => c switch
    {
        NumericUpDown nud => nud.Value.ToString(CultureInfo.InvariantCulture),
        TrackBar tb => tb.Value.ToString(CultureInfo.InvariantCulture),
        ProgressBar pb => pb.Value.ToString(CultureInfo.InvariantCulture),
        _ => null
    };

    private static string? WriteRange(Control c, object? value)
    {
        switch (c)
        {
            case NumericUpDown nud when decimal.TryParse(value?.ToString(), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var d):
                nud.Value = d;
                return nud.Value.ToString(CultureInfo.InvariantCulture);

            case TrackBar tb when int.TryParse(value?.ToString(), out var i):
                tb.Value = Math.Clamp(i, tb.Minimum, tb.Maximum);
                return tb.Value.ToString(CultureInfo.InvariantCulture);

            case ProgressBar pb when int.TryParse(value?.ToString(), out var i):
                pb.Value = Math.Clamp(i, pb.Minimum, pb.Maximum);
                return pb.Value.ToString(CultureInfo.InvariantCulture);

            default:
                return null;
        }
    }
}
