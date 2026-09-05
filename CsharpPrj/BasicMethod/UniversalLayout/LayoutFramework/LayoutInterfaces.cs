#nullable enable
using System;
using System.Collections.Generic;

namespace UniversalLayoutFramework
{
    public enum LayoutType { Proportional, Absolute, Grid, Stack, Flow }

    public enum StackDirection { Vertical, Horizontal, Grid }

    public enum LogLevel { Trace, Debug, Information, Warning, Error, Critical }

    public interface ILayoutContainer
    {
        string Name { get; }
        int Width { get; }
        int Height { get; }
        object? Tag { get; set; }
        IEnumerable<ILayoutElement> GetChildren();
        ILayoutElement? FindChild(string name);
    }

    public interface ILayoutElement
    {
        string Name { get; set; }
        int X { get; set; }
        int Y { get; set; }
        int Width { get; set; }
        int Height { get; set; }
        object? Tag { get; set; }
        bool HasChildren { get; }
        IEnumerable<ILayoutElement> GetChildren();
        void SetBounds(int x, int y, int width, int height);
        void SuspendLayout();
        void ResumeLayout(bool performLayout = true);
    }

    public interface ILayoutCalculator
    {
        LayoutOutput Calculate(LayoutDefinition definition, LayoutInput input);
        ElementOutput CalculateElement(ElementDefinition element, LayoutInput input);
    }

    public interface ILayoutValidator
    {
        bool ValidateDefinition(LayoutDefinition definition, out List<string> errors);
        bool ValidateElement(ElementDefinition element, out List<string> errors);
    }

    public interface ILayoutStrategy
    {
        string Name { get; }
        LayoutType LayoutType { get; }
        LayoutOutput Calculate(LayoutDefinition definition, LayoutInput input);
    }

    public delegate List<ElementOutput> OverlapResolverDelegate(List<ElementOutput> elements, int parentWidth, int parentHeight);

    public interface ILayoutLogger
    {
        void Log(LogLevel level, string message);
        bool IsEnabled(LogLevel level);
    }

    public interface IResizeNotifiable
    {
        string Name { get; }
        int Width { get; }
        int Height { get; }
        void SubscribeSizeChanged(Action onValidSize);
        void UnsubscribeSizeChanged();
    }
}
