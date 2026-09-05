
using CommandFramework.Abstractions;
using ForntEndDriven.Implementation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FileDriven.UI.Testes.ControlServices;

[TestClass]
public class CompositeValueWriterTests
{

    private class MockWriter : IControlValueWriter
    {
        private readonly Type _supportedType;
        public string? LastWrittenValue { get; private set; }
        public int WriteCount { get; private set; }

        public MockWriter(Type supportedType)
        {
            _supportedType = supportedType;
        }

        public bool WriteValue(object control, string? value)
        {
            if (!_supportedType.IsInstanceOfType(control))
                return false;
            LastWrittenValue = value;
            WriteCount++;
            return true;
        }

        public bool WriteValue(object control, object? value)
        {
            return WriteValue(control, value?.ToString());
        }

        public string? ReadValue(object control)
            => _supportedType.IsInstanceOfType(control) ? "mock-value" : null;

        public bool SupportsType(object control)
            => _supportedType.IsInstanceOfType(control);

        public void RegisterAdapter<TControl>(
            Func<TControl, string?, bool> writer,
            Func<TControl, string?> reader) where TControl : class
        {
        }
    }


    [TestMethod]
    public void AddWriter_ThenWriteValue_FirstSupportedWriterUsed()
    {
        var composite = new CompositeValueWriter();
        var writer1 = new MockWriter(typeof(string));
        var writer2 = new MockWriter(typeof(Button));
        composite.AddWriter(writer1);
        composite.AddWriter(writer2);
        var button = new Button();

        var result = composite.WriteValue(button, "test");

        Assert.IsTrue(result);
        Assert.AreEqual("test", writer2.LastWrittenValue);
        Assert.AreEqual(0, writer1.WriteCount);
    }

    [TestMethod]
    public void AddWriter_NoSupportedWriter_FallsBackToWinFormsControlService()
    {
        var composite = new CompositeValueWriter();
        composite.AddWriter(new MockWriter(typeof(string)));
        var button = new Button { Text = "original" };

        var result = composite.WriteValue(button, "new text");

        Assert.IsTrue(result);
        Assert.AreEqual("new text", button.Text);
    }


    [TestMethod]
    public void NoWriters_WriteValue_FallsBackToWinFormsControlService()
    {
        var composite = new CompositeValueWriter();
        var textBox = new TextBox { Text = "original" };

        var result = composite.WriteValue(textBox, "new value");

        Assert.IsTrue(result);
        Assert.AreEqual("new value", textBox.Text);
    }

    [TestMethod]
    public void NoWriters_ReadValue_ReturnsNull()
    {
        var composite = new CompositeValueWriter();
        var button = new Button();

        var result = composite.ReadValue(button);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void NoWriters_SupportsType_ReturnsFalse()
    {
        var composite = new CompositeValueWriter();
        var button = new Button();

        var result = composite.SupportsType(button);

        Assert.IsFalse(result);
    }


    [TestMethod]
    public void RemoveWriter_AfterRemoval_WriterNotUsed()
    {
        var composite = new CompositeValueWriter();
        var writer1 = new MockWriter(typeof(Button));
        var writer2 = new MockWriter(typeof(Button));
        composite.AddWriter(writer1);
        composite.AddWriter(writer2);
        composite.RemoveWriter(writer1);
        var button = new Button();

        var result = composite.WriteValue(button, "test");

        Assert.IsTrue(result);
        Assert.AreEqual(0, writer1.WriteCount);
        Assert.AreEqual(1, writer2.WriteCount);
    }

    [TestMethod]
    public void ClearWriters_AfterClear_FallsBackToWinFormsControlService()
    {
        var composite = new CompositeValueWriter();
        composite.AddWriter(new MockWriter(typeof(Button)));
        composite.ClearWriters();
        var textBox = new TextBox { Text = "original" };

        var result = composite.WriteValue(textBox, "new value");

        Assert.IsTrue(result);
        Assert.AreEqual("new value", textBox.Text);
    }


    [TestMethod]
    public void SupportsType_AnyWriterSupports_ReturnsTrue()
    {
        var composite = new CompositeValueWriter();
        composite.AddWriter(new MockWriter(typeof(string)));
        composite.AddWriter(new MockWriter(typeof(Button)));

        Assert.IsTrue(composite.SupportsType(new Button()));
        Assert.IsTrue(composite.SupportsType("hello"));
        Assert.IsFalse(composite.SupportsType(123));
    }


    [TestMethod]
    public void ReadValue_FirstSupportedWriter_ReturnsValue()
    {
        var composite = new CompositeValueWriter();
        composite.AddWriter(new MockWriter(typeof(string)));
        composite.AddWriter(new MockWriter(typeof(Button)));

        var result = composite.ReadValue(new Button());

        Assert.AreEqual("mock-value", result);
    }

    [TestMethod]
    public void ReadValue_NoSupportedWriter_ReturnsNull()
    {
        var composite = new CompositeValueWriter();
        composite.AddWriter(new MockWriter(typeof(string)));

        var result = composite.ReadValue(new Button());

        Assert.IsNull(result);
    }
}
