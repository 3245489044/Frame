using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using EventTriggerFramework.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventTriggerFramework.Tests
{
    [TestClass]
    public class ValueAccessorTests : TestBase
    {
        [TestMethod]
        public void RunAll_AdapterRegistration()
        {
            var result = RunTestGroup("AdapterRegistration", builder =>
            {
                builder.Add("RegisterAdapter_ShouldStoreAdapter", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    accessor.RegisterAdapter<TestControl>(c => c.Value, (c, v) => c.Value = v?.ToString());
                    return accessor.IsSupported(new TestControl());
                });

                builder.Add("RegisterAdapter_NullGetter_ShouldThrow", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    try
                    {
                        accessor.RegisterAdapter<TestControl>(null!, (c, v) => { });
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });

                builder.Add("RegisterAdapter_NullSetter_ShouldThrow", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    try
                    {
                        accessor.RegisterAdapter<TestControl>(c => c.Value, null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });

                builder.Add("RegisterAdapterWithResult_ShouldStoreAdapter", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    accessor.RegisterAdapterWithResult<TestControl>(
                        c => c.Value,
                        (c, v) => { c.Value = v?.ToString(); return Result<object>.Success("ok"); });
                    return accessor.IsSupported(new TestControl());
                });

                builder.Add("ClearAdapters_ShouldRemoveAll", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    accessor.RegisterAdapter<TestControl>(c => c.Value, (c, v) => c.Value = v?.ToString());
                    accessor.ClearAdapters();
                    return !accessor.IsSupported(new TestControl());
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_GetSetValue()
        {
            var result = RunTestGroup("GetSetValue", builder =>
            {
                builder.Add("GetValue_ShouldReturnValue", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    accessor.RegisterAdapter<TestControl>(c => c.Value, (c, v) => c.Value = v?.ToString());
                    var control = new TestControl { Value = "hello" };
                    return accessor.GetValue(control) as string == "hello";
                });

                builder.Add("SetValue_ShouldSetValue", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    accessor.RegisterAdapter<TestControl>(c => c.Value, (c, v) => c.Value = v?.ToString());
                    var control = new TestControl();
                    accessor.SetValue(control, "world");
                    return control.Value == "world";
                });

                builder.Add("GetValue_NullTarget_ShouldThrow", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    try
                    {
                        accessor.GetValue(null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });

                builder.Add("SetValue_NullTarget_ShouldThrow", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    try
                    {
                        accessor.SetValue(null!, "value");
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });

                builder.Add("GetValue_UnsupportedType_ShouldThrow", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    try
                    {
                        accessor.GetValue(new UnsupportedControl());
                        return false;
                    }
                    catch (NotSupportedException)
                    {
                        return true;
                    }
                });

                builder.Add("SetValue_UnsupportedType_ShouldThrow", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    try
                    {
                        accessor.SetValue(new UnsupportedControl(), "value");
                        return false;
                    }
                    catch (NotSupportedException)
                    {
                        return true;
                    }
                });

                builder.Add("GetValue_WrongType_ShouldThrow", () =>
                {
                    var accessor = new ControlValueAccessorBase<TestControl>();
                    try
                    {
                        accessor.GetValue("not a control");
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                });

                builder.Add("GetValue_Typed_ShouldConvert", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    accessor.RegisterAdapter<TestControl>(c => c.Value, (c, v) => c.Value = v?.ToString());
                    var control = new TestControl { Value = "42" };
                    var result = accessor.GetValue<int>(control);
                    return result == 42;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_IsSupported()
        {
            var result = RunTestGroup("IsSupported", builder =>
            {
                builder.Add("IsSupported_RegisteredType_ShouldReturnTrue", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    accessor.RegisterAdapter<TestControl>(c => c.Value, (c, v) => c.Value = v?.ToString());
                    return accessor.IsSupported(new TestControl());
                });

                builder.Add("IsSupported_UnregisteredType_ShouldReturnFalse", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    return !accessor.IsSupported(new UnsupportedControl());
                });

                builder.Add("IsSupported_NullTarget_ShouldThrow", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    try
                    {
                        accessor.IsSupported(null!);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });

                builder.Add("IsSupported_WrongBaseType_ShouldReturnFalse", () =>
                {
                    var accessor = new ControlValueAccessorBase<TestControl>();
                    return !accessor.IsSupported("not a control");
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_InheritanceSupport()
        {
            var result = RunTestGroup("InheritanceSupport", builder =>
            {
                builder.Add("RegisterAdapter_BaseType_ShouldSupportDerivedType", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    accessor.RegisterAdapter<BaseControl>(c => c.BaseValue, (c, v) => c.BaseValue = v?.ToString());
                    var derived = new DerivedControl { BaseValue = "inherited" };
                    return accessor.GetValue(derived) as string == "inherited";
                });

                builder.Add("RegisterAdapter_DerivedType_ShouldNotAffectBaseType", () =>
                {
                    var accessor = new ControlValueAccessorBase<object>();
                    accessor.RegisterAdapter<DerivedControl>(c => c.DerivedValue, (c, v) => c.DerivedValue = v?.ToString());
                    var baseCtrl = new BaseControl();
                    return !accessor.IsSupported(baseCtrl);
                });
            });

            result.AssertAllPassed();
        }

        public class TestControl
        {
            public string? Value { get; set; }
        }

        public class UnsupportedControl
        {
            public string? Name { get; set; }
        }

        public class BaseControl
        {
            public string? BaseValue { get; set; }
        }

        public class DerivedControl : BaseControl
        {
            public string? DerivedValue { get; set; }
        }
    }
}
