
using ParameterResolver.Abstractions;
using ParameterResolver.Converter;

namespace ParameterResolver.Tests.Converter;

[TestClass]
public class ValueConverterRegistryTests
{
    private readonly ConverterRegistry _registry = new();

    [TestMethod]
    public void Register_ShouldAddConverter()
    {
        _registry.Register(typeof(int), new StringToIntConverter());

        var converter = _registry.GetConverter(typeof(int));

        Assert.IsNotNull(converter);
    }

    [TestMethod]
    public void GetConverter_NotRegistered_ShouldReturnNull()
    {
        var converter = _registry.GetConverter(typeof(int));

        Assert.IsNull(converter);
    }

    [TestMethod]
    public void Register_OverrideExisting_ShouldReplace()
    {
        _registry.Register(typeof(int), new StringToIntConverter());
        _registry.Register(typeof(int), new StringToIntConverter());

        var converter = _registry.GetConverter(typeof(int));

        Assert.IsNotNull(converter);
    }

    [TestMethod]
    public void HasConverter_Registered_ShouldReturnTrue()
    {
        _registry.Register(typeof(int), new StringToIntConverter());

        Assert.IsTrue(_registry.HasConverter(typeof(int)));
    }

    [TestMethod]
    public void HasConverter_NotRegistered_ShouldReturnFalse()
    {
        Assert.IsFalse(_registry.HasConverter(typeof(int)));
    }

    [TestMethod]
    public void GetAllConverters_ShouldReturnAll()
    {
        _registry.Register(typeof(int), new StringToIntConverter());
        _registry.Register(typeof(bool), new StringToBoolConverter());

        var all = _registry.GetAllConverters().ToList();

        Assert.AreEqual(2, all.Count);
    }

    private class StringToIntConverter : IValueConverter
    {
        public Type TargetType => typeof(int);

        public object? Convert(IReadOnlyDictionary<string, object?> inputs, ConversionContext context)
        {
            if (inputs.TryGetValue("value", out var val) && val is string s)
                return int.Parse(s);
            return null;
        }
    }

    private class StringToBoolConverter : IValueConverter
    {
        public Type TargetType => typeof(bool);

        public object? Convert(IReadOnlyDictionary<string, object?> inputs, ConversionContext context)
        {
            if (inputs.TryGetValue("value", out var val) && val is string s)
                return bool.Parse(s);
            return null;
        }
    }
}
