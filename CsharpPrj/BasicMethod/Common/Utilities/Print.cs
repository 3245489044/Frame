namespace Common.GraphMindStudio.Print
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public static class Printer
    {
        public static void DebugPrint(object obj, bool debugpath = false, string title = "")
        {
            if (debugpath)
            {
                Console.WriteLine($"[{title}] {obj}");
            }
        }

        public static string PrintCollection(object obj, string title = "", bool debugpath = false)
        {
            if (obj is IEnumerable<object> list)
            {
                return string.Join(", ", list.Select(item => item?.ToString() ?? "null"));
            }
            return obj?.ToString() ?? "null";
        }
    }
}
