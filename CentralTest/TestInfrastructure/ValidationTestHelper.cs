
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestInfrastructure
{

    public static class ValidationTestHelper
    {
        public static bool TestNullArgument(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (ArgumentNullException)
            {
                return true;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentNullException)
            {
                return true;
            }
        }

        public static async Task<bool> TestNullArgumentAsync(Func<Task> asyncAction)
        {
            try
            {
                await asyncAction();
                return false;
            }
            catch (ArgumentNullException)
            {
                return true;
            }
        }

        public static bool TestEmptyStringArgument(Action<string> action, string? emptyValue = null)
        {
            var testValue = emptyValue ?? string.Empty;
            try
            {
                action(testValue);
                return false;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }

        public static bool TestWhitespaceArgument(Action<string> action)
        {
            try
            {
                action("   ");
                return false;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }

        public static (bool nullThrows, bool emptyThrows, bool whitespaceThrows) TestNullOrEmptyArguments(
            Action<string?> nullAction,
            Action<string> emptyAction,
            Action<string> whitespaceAction)
        {
            var nullResult = TestNullArgument(() => nullAction(null));
            var emptyResult = TestEmptyStringArgument(emptyAction);
            var whitespaceResult = TestWhitespaceArgument(whitespaceAction);
            return (nullResult, emptyResult, whitespaceResult);
        }

        public static bool TestNonExistentReturnsDefault<T>(Func<string, T?> lookup, T? expectedDefault = default)
        {
            var result = lookup("nonExistentKey_" + Guid.NewGuid().ToString("N"));
            return EqualityComparer<T?>.Default.Equals(result, expectedDefault);
        }

        public static bool TestNonExistentReturnsFalse(Func<string, bool> lookup)
        {
            return !lookup("nonExistentKey_" + Guid.NewGuid().ToString("N"));
        }

        public static bool TestOverwriteRegistration(Action register)
        {
            try
            {
                register();
                register();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TestClearEmpties(Func<bool> isEmptyCheck, Action clear)
        {
            clear();
            return isEmptyCheck();
        }

        public static bool TestMultipleDispose(Action dispose)
        {
            try
            {
                dispose();
                dispose();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TestRegistrationLifecycle(
            Action<string> register,
            Func<string, bool> contains,
            Action<string> unregister)
        {
            var name = "testKey_" + Guid.NewGuid().ToString("N");

            if (contains(name))
                return false;

            register(name);
            if (!contains(name))
                return false;

            unregister(name);
            if (contains(name))
                return false;

            return true;
        }

        public static bool TestUnregisterNonExistent(Action<string> unregister)
        {
            try
            {
                unregister("nonExistent_" + Guid.NewGuid().ToString("N"));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TestGetRegisteredNames(
            Action<string> register,
            Func<string[]> getNames,
            params string[] expectedNames)
        {
            foreach (var name in expectedNames)
                register(name);

            var names = getNames();
            return expectedNames.All(n => names.Contains(n))
                && names.Length >= expectedNames.Length;
        }
    }


    public static class EnumTestHelper
    {
        public static void TestEnumMessageMapping<TEnum>(
            (TEnum value, string expectedMessage)[] mappings,
            Func<TEnum, string> messageResolver)
            where TEnum : Enum
        {
            foreach (var (value, expectedMessage) in mappings)
            {
                var actual = messageResolver(value);
                Assert.AreEqual(expectedMessage, actual,
                    $"枚举值 {value} 期望消息 '{expectedMessage}'，实际 '{actual}'");
            }
        }

        public static void TestEnumPredicate<TEnum>(
            TEnum value, Func<TEnum, bool> predicate, bool expectedResult)
            where TEnum : Enum
        {
            var actual = predicate(value);
            Assert.AreEqual(expectedResult, actual,
                $"枚举值 {value} 期望谓词结果 {expectedResult}，实际 {actual}");
        }

        public static void TestEnumPredicates<TEnum>(
            (TEnum value, bool expectedResult)[] testCases,
            Func<TEnum, bool> predicate)
            where TEnum : Enum
        {
            foreach (var (value, expectedResult) in testCases)
            {
                var actual = predicate(value);
                Assert.AreEqual(expectedResult, actual,
                    $"枚举值 {value} 期望谓词结果 {expectedResult}，实际 {actual}");
            }
        }

        public static void TestUnknownEnumValue<TEnum>(
            Func<TEnum, string> messageResolver,
            string expectedDefaultMessage) where TEnum : Enum
        {
            var unknownValue = (TEnum)Enum.ToObject(typeof(TEnum), 9999);
            var actual = messageResolver(unknownValue);
            Assert.AreEqual(expectedDefaultMessage, actual,
                $"未知枚举值期望默认消息 '{expectedDefaultMessage}'，实际 '{actual}'");
        }

        public static void TestEnumClassification<TEnum>(
            TEnum[] expectedTrueValues,
            TEnum[] expectedFalseValues,
            Func<TEnum, bool> classifier)
            where TEnum : Enum
        {
            foreach (var value in expectedTrueValues)
            {
                Assert.IsTrue(classifier(value),
                    $"枚举值 {value} 期望分类结果为 true");
            }

            foreach (var value in expectedFalseValues)
            {
                Assert.IsFalse(classifier(value),
                    $"枚举值 {value} 期望分类结果为 false");
            }
        }
    }


    public static class LogCategoryTestHelper
    {
        public static void TestResolveMapping<TEnum>(
            (string input, TEnum expected)[] mappings,
            Func<string, TEnum> resolver)
            where TEnum : struct, Enum
        {
            foreach (var (input, expected) in mappings)
            {
                var actual = resolver(input);
                Assert.AreEqual(expected, actual,
                    $"输入 '{input}' 期望解析为 {expected}，实际为 {actual}");
            }
        }

        public static void TestResolveDefault<TEnum>(
            string[] invalidInputs,
            Func<string, TEnum> resolver,
            TEnum expectedDefault)
            where TEnum : struct, Enum
        {
            foreach (var input in invalidInputs)
            {
                var actual = resolver(input);
                Assert.AreEqual(expectedDefault, actual,
                    $"无效输入 '{input ?? "null"}' 期望返回默认值 {expectedDefault}，实际为 {actual}");
            }
        }

        public static void TestModuleNameResolution<TEnum>(
            (TEnum category, string expectedModuleName)[] mappings,
            Func<TEnum, string> moduleNameResolver)
            where TEnum : struct, Enum
        {
            foreach (var (category, expectedModuleName) in mappings)
            {
                var actual = moduleNameResolver(category);
                Assert.AreEqual(expectedModuleName, actual,
                    $"分类 {category} 期望模块名 '{expectedModuleName}'，实际 '{actual}'");
            }
        }

        public static void TestDotNotationResolution<TEnum>(
            (string dotNotation, TEnum expected)[] mappings,
            Func<string, TEnum> resolver)
            where TEnum : struct, Enum
        {
            foreach (var (dotNotation, expected) in mappings)
            {
                var actual = resolver(dotNotation);
                Assert.AreEqual(expected, actual,
                    $"点号表示法 '{dotNotation}' 期望解析为 {expected}，实际为 {actual}");
            }
        }

        public static void TestShortNameResolution<TEnum>(
            (string shortName, TEnum expected)[] mappings,
            Func<string, TEnum> resolver)
            where TEnum : struct, Enum
        {
            foreach (var (shortName, expected) in mappings)
            {
                var actual = resolver(shortName);
                Assert.AreEqual(expected, actual,
                    $"短名称 '{shortName}' 期望解析为 {expected}，实际为 {actual}");
            }
        }
    }
}
