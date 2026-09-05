#nullable enable

using System;
using System.Collections.Generic;

namespace Modules.ReflectionMethod
{
    public static class ReflectionConfig
    {
        public static class Separators
        {
            public static readonly char[] NameSplitChars = { '_', '.', '-' };
            public const char Dot = '.';
            public const string Comma = ", ";
            public const string Pipe = "|";
        }

        public static class NumericConstants
        {
            public const int ProximityThreshold = 50;
            public const int DirectionThreshold = 5;
            public const int MaxDictionaryKeys = 10;
            public const int DefaultMaxDepth = 3;
        }

        public static class FormatStrings
        {
            public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
            public const string RectangleFormat = "Rectangle({0},{1},{2},{3})";
            public const string PointFormat = "Point({0},{1})";
            public const string SizeFormat = "Size({0},{1})";
            public const string DictionaryFormat = "Dictionary[{0}]";
            public const string CollectionFormat = "Collection[{0}]";
            public const string TypeNameFormat = "[{0}]";
        }
    }
}
