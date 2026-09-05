
using ForntEndDriven.Implementation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FileDriven.UI.Testes.ControlServices;

[TestClass]
public class WinFormsControlValueWriterTests
{

    private static WinFormsControlValueWriter CreateWriterWithExtendedAdapters()
    {
        var writer = new WinFormsControlValueWriter();

        writer.RegisterAdapter<Button>(
            writer: (btn, value) =>
            {
                btn.Text = value ?? string.Empty;
                return true;
            },
            reader: (btn) => btn.Text);

        writer.RegisterAdapter<CheckBox>(
            writer: (cb, value) =>
            {
                if (bool.TryParse(value, out var isChecked))
                    cb.Checked = isChecked;
                else
                    cb.Checked = false;
                return true;
            },
            reader: (cb) => cb.Checked.ToString().ToLower());

        writer.RegisterAdapter<ComboBox>(
            writer: (combo, value) =>
            {
                if (value != null && combo.Items.Contains(value))
                    combo.SelectedItem = value;
                return true;
            },
            reader: (combo) => combo.SelectedItem?.ToString());

        return writer;
    }


    [TestMethod]
    public void SupportsType_TextBox_ReturnsTrue()
    {
        var writer = new WinFormsControlValueWriter();
        Assert.IsTrue(writer.SupportsType(new TextBox()));
    }

    [TestMethod]
    public void SupportsType_Label_ReturnsTrue()
    {
        var writer = new WinFormsControlValueWriter();
        Assert.IsTrue(writer.SupportsType(new Label()));
    }

    [TestMethod]
    public void SupportsType_ListView_ReturnsTrue()
    {
        var writer = new WinFormsControlValueWriter();
        Assert.IsTrue(writer.SupportsType(new ListView()));
    }

    [TestMethod]
    public void SupportsType_RichTextBox_ReturnsTrue()
    {
        var writer = new WinFormsControlValueWriter();
        Assert.IsTrue(writer.SupportsType(new RichTextBox()));
    }


    [TestMethod]
    public void SupportsType_Button_AfterRegistration_ReturnsTrue()
    {
        var writer = CreateWriterWithExtendedAdapters();
        Assert.IsTrue(writer.SupportsType(new Button()));
    }

    [TestMethod]
    public void SupportsType_CheckBox_AfterRegistration_ReturnsTrue()
    {
        var writer = CreateWriterWithExtendedAdapters();
        Assert.IsTrue(writer.SupportsType(new CheckBox()));
    }

    [TestMethod]
    public void SupportsType_ComboBox_AfterRegistration_ReturnsTrue()
    {
        var writer = CreateWriterWithExtendedAdapters();
        Assert.IsTrue(writer.SupportsType(new ComboBox()));
    }


    [TestMethod]
    public void SupportsType_UnsupportedType_ReturnsFalse()
    {
        var writer = new WinFormsControlValueWriter();
        Assert.IsFalse(writer.SupportsType("not a control"));
        Assert.IsFalse(writer.SupportsType(123));
        Assert.IsFalse(writer.SupportsType(new object()));
    }

    [TestMethod]
    public void SupportsType_Null_ReturnsFalse()
    {
        var writer = new WinFormsControlValueWriter();
        Assert.IsFalse(writer.SupportsType(null!));
    }


    [TestMethod]
    public void WriteValue_TextBox_SetsText()
    {
        var writer = new WinFormsControlValueWriter();
        var textBox = new TextBox();

        var result = writer.WriteValue(textBox, "hello world");

        Assert.IsTrue(result);
        Assert.AreEqual("hello world", textBox.Text);
    }

    [TestMethod]
    public void ReadValue_TextBox_ReturnsText()
    {
        var writer = new WinFormsControlValueWriter();
        var textBox = new TextBox { Text = "test value" };

        var result = writer.ReadValue(textBox);

        Assert.AreEqual("test value", result);
    }

    [TestMethod]
    public void WriteValue_TextBox_NullValue_ClearsText()
    {
        var writer = new WinFormsControlValueWriter();
        var textBox = new TextBox { Text = "existing" };

        var result = writer.WriteValue(textBox, null);

        Assert.IsTrue(result);
        Assert.AreEqual("", textBox.Text);
    }


    [TestMethod]
    public void WriteValue_Button_AfterRegistration_SetsText()
    {
        var writer = CreateWriterWithExtendedAdapters();
        var button = new Button();

        var result = writer.WriteValue(button, "Click Me");

        Assert.IsTrue(result);
        Assert.AreEqual("Click Me", button.Text);
    }

    [TestMethod]
    public void ReadValue_Button_AfterRegistration_ReturnsText()
    {
        var writer = CreateWriterWithExtendedAdapters();
        var button = new Button { Text = "Submit" };

        var result = writer.ReadValue(button);

        Assert.AreEqual("Submit", result);
    }


    [TestMethod]
    public void WriteValue_CheckBox_AfterRegistration_True_ChecksIt()
    {
        var writer = CreateWriterWithExtendedAdapters();
        var checkBox = new CheckBox();

        var result = writer.WriteValue(checkBox, "true");

        Assert.IsTrue(result);
        Assert.IsTrue(checkBox.Checked);
    }

    [TestMethod]
    public void WriteValue_CheckBox_AfterRegistration_False_UnchecksIt()
    {
        var writer = CreateWriterWithExtendedAdapters();
        var checkBox = new CheckBox { Checked = true };

        var result = writer.WriteValue(checkBox, "false");

        Assert.IsTrue(result);
        Assert.IsFalse(checkBox.Checked);
    }

    [TestMethod]
    public void ReadValue_CheckBox_AfterRegistration_Checked_ReturnsTrue()
    {
        var writer = CreateWriterWithExtendedAdapters();
        var checkBox = new CheckBox { Checked = true };

        var result = writer.ReadValue(checkBox);

        Assert.AreEqual("true", result);
    }

    [TestMethod]
    public void ReadValue_CheckBox_AfterRegistration_Unchecked_ReturnsFalse()
    {
        var writer = CreateWriterWithExtendedAdapters();
        var checkBox = new CheckBox { Checked = false };

        var result = writer.ReadValue(checkBox);

        Assert.AreEqual("false", result);
    }


    [TestMethod]
    public void WriteValue_ComboBox_AfterRegistration_SetsSelectedItem()
    {
        var writer = CreateWriterWithExtendedAdapters();
        var comboBox = new ComboBox();
        comboBox.Items.AddRange(new[] { "A", "B", "C" });

        var result = writer.WriteValue(comboBox, "B");

        Assert.IsTrue(result);
        Assert.AreEqual("B", comboBox.SelectedItem);
    }

    [TestMethod]
    public void ReadValue_ComboBox_AfterRegistration_ReturnsSelectedItem()
    {
        var writer = CreateWriterWithExtendedAdapters();
        var comboBox = new ComboBox();
        comboBox.Items.AddRange(new[] { "A", "B", "C" });
        comboBox.SelectedItem = "C";

        var result = writer.ReadValue(comboBox);

        Assert.AreEqual("C", result);
    }


    [TestMethod]
    public void WriteValue_Label_SetsText()
    {
        var writer = new WinFormsControlValueWriter();
        var label = new Label();

        var result = writer.WriteValue(label, "Status: OK");

        Assert.IsTrue(result);
        Assert.AreEqual("Status: OK", label.Text);
    }

    [TestMethod]
    public void ReadValue_Label_ReturnsText()
    {
        var writer = new WinFormsControlValueWriter();
        var label = new Label { Text = "Ready" };

        var result = writer.ReadValue(label);

        Assert.AreEqual("Ready", result);
    }


    [TestMethod]
    public void WriteValue_UnsupportedControl_ReturnsFalse()
    {
        var writer = new WinFormsControlValueWriter();
        var result = writer.WriteValue("not a control", "test");
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void ReadValue_UnsupportedControl_ReturnsNull()
    {
        var writer = new WinFormsControlValueWriter();
        var result = writer.ReadValue("not a control");
        Assert.IsNull(result);
    }

    [TestMethod]
    public void WriteValue_NullControl_ReturnsFalse()
    {
        var writer = new WinFormsControlValueWriter();
        var result = writer.WriteValue(null!, "test");
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void ReadValue_NullControl_ReturnsNull()
    {
        var writer = new WinFormsControlValueWriter();
        var result = writer.ReadValue(null!);
        Assert.IsNull(result);
    }
}
