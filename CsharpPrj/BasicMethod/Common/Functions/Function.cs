#nullable disable 

namespace Common.GraphMindStudio.Function
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class DictionaryMapFuncs
    {
        public static void RegistMethod(Dictionary<string, object> _functionMap)
        {
        }

        public Dictionary<string, object> MappingFunction(Dictionary<string, object> _functionMap)
        {
            _functionMap = new()
                {
                        { "FrameFuncPipeline" ,      CreateAction((obj)=> {})                  },
                        { "GenericParameterSet",     CreateAction((obj)=> {})                 },
                        { "GenericParameterFrameSet",CreateAction((obj)=> {}) },
                        { "GetPathSetValue",         CreateAction((o)=> {})       },
                        { "ProcessSegments",         CreateAction(o => {}) },
                        { "StepExecution",           (Func<object>)(() => null)                                                                  },
                        { "CopyTable",               CreateAction(()=> {})                  },
                        { "RenameColumn",            CreateAction(()=> {})               },
                        { "AddColumn",               CreateAction(()=> {})               },
                        { "CheckFirstColumnForValue",   CreateAction(()=> {})              },
                        { "DropTable",               CreateAction(()=> {})  },
                        { "CreateTable",             CreateAction(()=> {})      },
                        { "WebCallProcess",           (Func<object>)(() => null)                                                                },
                        { "LoadAICallNodeFromDB",     CreateAction(()=> {})},
                        {"Int",                      Arithmetics.Int                                                                                  },
                        {"Float",                    Arithmetics.Float                                                                                },
                        {"Double",                   Arithmetics.Double                                                                   },
                        {"String",                   Arithmetics.String                                                                   },
                        {"StringToDictionary",       Arithmetics.StringToDictionary                                                                   },
                        {"Plus",                     Arithmetics.Plus                                                                                 },
                        {"RandomIntFunc",            Arithmetics.RandomIntFunc                                                                        },
                        {"RandomFloatFunc",          Arithmetics.RandomFloatFunc                                                                      },
                        {"RandomDoubleFunc",         Arithmetics.RandomDoubleFunc                                                                     },
            };

            RegistMethod(_functionMap);

            return _functionMap;
        }

        public static Action<object> CreateAction(Action<object> action)
        {
            return action;
        }

        public static Action CreateAction(Action action)
        {
            return action;
        }
    }

    public static class Arithmetics
    {
        public static Func<object, object> Int = (obj) => Convert.ToInt32(obj);
        public static Func<object, object> Float = (obj) => Convert.ToSingle(obj);
        public static Func<object, object> Double = (obj) => Convert.ToDouble(obj);
        public static Func<object, object> String = (obj) => obj.ToString();

        public static readonly Func<string, bool> StringToBool = str =>
        {
            if (string.IsNullOrEmpty(str)) return false;
            return str.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   str.Equals("1") ||
                   str.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   str.Equals("on", StringComparison.OrdinalIgnoreCase);
        };

        public static Func<object, object> StringToDictionary = (obj) =>
        {
            if (obj is Dictionary<string, object> dic) return dic;
            var input = obj?.ToString() ?? "";
            var kvs = ParseKeyValuePairs(input);
            return kvs;
        };

        public static Func<object, object> Plus = (obj) =>
        {
            List<object> list = (List<object>)obj;
            object value1 = list[0];
            object value2 = list[1];

            string str1 = Convert.ToString(value1);
            string str2 = Convert.ToString(value2);

            if (double.TryParse(str1, out double d1) &&
                double.TryParse(str2, out double d2))
            {
                return (d1 + d2);
            }
            return $"Invalid input\n"
                   + $"value1: {list[0]} ({list[0].GetType()})\n"
                   + $"value2: {list[1]} ({list[1].GetType()})";
        };

        public static Func<object, object> RandomIntFunc = obj => RandomInRange<int>(obj);
        public static Func<object, object> RandomFloatFunc = obj => RandomInRange<float>(obj);
        public static Func<object, object> RandomDoubleFunc = obj => RandomInRange<double>(obj);

        public static T RandomInRange<T>(object obj)
        {
            if (obj is List<object> list && list.Count >= 2)
            {
                double min = Convert.ToDouble(list[0]);
                double max = Convert.ToDouble(list[1]);
                var rand = new System.Random();
                if (typeof(T) == typeof(int))
                {
                    return (T)(object)rand.Next((int)min, (int)max);
                }
                else if (typeof(T) == typeof(float))
                {
                    return (T)(object)((float)(rand.NextDouble() * (max - min) + min));
                }
                else if (typeof(T) == typeof(double))
                {
                    return (T)(object)(rand.NextDouble() * (max - min) + min);
                }
            }
            if (typeof(T) == typeof(int)) return (T)(object)0;
            if (typeof(T) == typeof(float)) return (T)(object)0f;
            if (typeof(T) == typeof(double)) return (T)(object)0.0;
            return default;
        }

        private static Dictionary<string, object> ParseKeyValuePairs(string input)
        {
            var result = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(input)) return result;

            var pairs = input.Split(',');
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=');
                if (parts.Length == 2)
                {
                    result[parts[0].Trim()] = parts[1].Trim();
                }
            }
            return result;
        }
    }

    public static class InovkeActions
    {
        public static class Stringrelated
        {
            public static void ProcessSegments(Dictionary<string, object> dict, string input)
            {
                List<string> list = StringProcess.CharacterTools.SplitBySemicolonIgnoreBraces(input);
                foreach (var str in list)
                {
                    List<(string key, string value)> parameters = StringProcess.CharacterTools.SpliteKeyValue(str);
                    foreach (var param in parameters)
                    {
                    }
                }
            }
        }
    }
}
