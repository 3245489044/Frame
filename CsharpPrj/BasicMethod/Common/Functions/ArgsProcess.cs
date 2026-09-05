namespace Common.GraphMindStudio
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    public static class ArgsProcess
    {
        public static bool AddToArgsStrict(List<string> argsList, object inputValue, out string diagnosticInfo)
        {
            return AddToArgsStrictCore(argsList, inputValue, item => item?.ToString() ?? "", out diagnosticInfo);
        }

        public static bool AddToArgsStrict(IList<object> argsList, object inputValue, out string diagnosticInfo)
        {
            return AddToArgsStrictCore(argsList, inputValue, item => item, out diagnosticInfo);
        }

        private static bool AddToArgsStrictCore<T>(IList<T> argsList, object inputValue, Func<object, T> converter, out string diagnosticInfo)
        {
            diagnosticInfo = null;

            if (argsList == null)
                throw new ArgumentNullException(nameof(argsList));

            try
            {
                if (inputValue == null)
                {
                    diagnosticInfo = "输入值为null，不添加";
                    return false;
                }

                if (inputValue is string strValue)
                {
                    if (string.IsNullOrEmpty(strValue))
                    {
                        diagnosticInfo = "输入字符串为空，不添加";
                        return false;
                    }

                    argsList.Add(converter(strValue));
                    diagnosticInfo = $"添加字符串: '{strValue}'";
                    return true;
                }

                if (inputValue is IEnumerable enumerable && inputValue.GetType() != typeof(string))
                {
                    return ProcessEnumerableStrict(argsList, enumerable, converter, out diagnosticInfo);
                }

                return ProcessSingleValueStrict(argsList, inputValue, converter, out diagnosticInfo);
            }
            catch (Exception ex)
            {
                return HandleExceptionStrict(argsList, converter, ex, out diagnosticInfo);
            }
        }

        private static bool ProcessEnumerableStrict<T>(IList<T> argsList, IEnumerable enumerable,
            Func<object, T> converter, out string diagnosticInfo)
        {
            diagnosticInfo = null;
            int addedCount = 0;
            var enumerator = enumerable.GetEnumerator();

            if (!enumerator.MoveNext())
            {
                diagnosticInfo = "输入集合为空，不添加";
                return false;
            }

            do
            {
                argsList.Add(converter(enumerator.Current));
                addedCount++;
            }
            while (enumerator.MoveNext());

            diagnosticInfo = $"处理集合，添加{addedCount}项";
            return true;
        }

        private static bool ProcessSingleValueStrict<T>(IList<T> argsList, object inputValue, Func<object, T> converter, out string diagnosticInfo)
        {
            argsList.Add(converter(inputValue));
            diagnosticInfo = $"添加单个值: {inputValue}";
            return true;
        }

        private static bool HandleExceptionStrict<T>(IList<T> argsList, Func<object, T> converter, Exception ex, out string diagnosticInfo)
        {
            diagnosticInfo = $"处理时发生异常: {ex.Message}";
            return false;
        }

        public static void AddToArgs(List<string> argsList, object inputValue)
        {
            if (argsList == null || inputValue == null)
                return;

            if (inputValue is string strValue && !string.IsNullOrEmpty(strValue))
            {
                argsList.Add(strValue);
            }
            else if (inputValue is IEnumerable enumerable && inputValue.GetType() != typeof(string))
            {
                foreach (var item in enumerable)
                {
                    if (item != null)
                    {
                        argsList.Add(item.ToString() ?? "");
                    }
                }
            }
            else
            {
                argsList.Add(inputValue.ToString() ?? "");
            }
        }

        public static bool AddToArgs(Dictionary<string, object> inputValuesRef, string key, object value)
        {
            if (inputValuesRef == null || string.IsNullOrEmpty(key))
                return false;

            inputValuesRef[key] = value;
            return true;
        }
    }
}
