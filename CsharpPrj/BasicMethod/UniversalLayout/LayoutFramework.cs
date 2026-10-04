#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UniversalLayoutMath;

namespace UniversalLayoutFramework
{

    public class LayoutPosition
    {
        public double Left { get; set; } = -1;
        public double Top { get; set; } = -1;
        public double Width { get; set; } = -1;
        public double Height { get; set; } = -1;
    }

    public class SizeConstraints
    {
        public int MinWidth { get; set; } = 0;
        public int MinHeight { get; set; } = 0;
        public int MaxWidth { get; set; } = int.MaxValue;
        public int MaxHeight { get; set; } = int.MaxValue;
        public bool MaintainAspectRatio { get; set; } = false;
    }

    public class ZOrder
    {
        public int Order { get; set; } = 0;
        public bool BringToFront { get; set; } = false;
    }

    public class ElementDefinition
    {
        public string ControlName { get; set; } = string.Empty;
        public LayoutPosition Position { get; set; } = new();
        public SizeConstraints SizeConstraints { get; set; } = new();
        public ZOrder ZOrder { get; set; } = new();
    }

    public class LayoutDefinition
    {
        public string Name { get; set; } = string.Empty;
        public LayoutType Type { get; set; } = LayoutType.Proportional;
        public List<ElementDefinition> Elements { get; set; } = new();
        public LayoutConstraints Constraints { get; set; } = new();
    }

    public class LayoutConstraints
    {
        public int MinWidth { get; set; } = 0;
        public int MinHeight { get; set; } = 0;
        public int MaxWidth { get; set; } = int.MaxValue;
        public int MaxHeight { get; set; } = int.MaxValue;
        public bool AllowOverflow { get; set; } = false;
        public object? Tag { get; set; }
    }

    public class LayoutInput
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public int ParentWidth { get; }
        public int ParentHeight { get; }

        public LayoutInput(int x, int y, int width, int height, int parentWidth, int parentHeight)
        {
            X = x; Y = y; Width = width; Height = height;
            ParentWidth = parentWidth; ParentHeight = parentHeight;
        }
    }

    public record ElementOutput
    {
        public string ControlName { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int ZOrder { get; set; }
    }

    public class LayoutOutput
    {
        public bool IsValid { get; }
        public string? ErrorMessage { get; }
        public List<ElementOutput>? Elements { get; }

        public LayoutOutput(bool isValid, string? errorMessage, List<ElementOutput>? elements = null)
        {
            IsValid = isValid; ErrorMessage = errorMessage; Elements = elements;
        }

        public static LayoutOutput Success(List<ElementOutput> elements) => new(true, null, elements);
        public static LayoutOutput Failure(string error) => new(false, error);
    }

    public class SmartLayoutConfig
    {
        public bool EnableResizeHandling { get; set; } = true;
        public bool Recursive { get; set; } = true;
        public bool LogEnabled { get; set; } = false;
        public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(50);
        public OverlapResolverDelegate? OverlapResolver { get; set; }
    }


    public class ConsoleLayoutLogger : ILayoutLogger
    {
        private readonly string _category;

        public ConsoleLayoutLogger(string category = "Layout") { _category = category; }

        public void Log(LogLevel level, string message)
        {
            var prefix = level switch
            {
                LogLevel.Trace => "[TRC]", LogLevel.Debug => "[DBG]",
                LogLevel.Information => "[INF]", LogLevel.Warning => "[WRN]",
                LogLevel.Error => "[ERR]", LogLevel.Critical => "[CRT]",
                _ => "[???]"
            };

        }

        public bool IsEnabled(LogLevel level) => true;
    }


    public class ProportionalLayoutStrategy : ILayoutStrategy
    {
        public string Name => "Proportional";
        public LayoutType LayoutType => LayoutType.Proportional;

        public LayoutOutput Calculate(LayoutDefinition definition, LayoutInput input)
        {
            if (definition.Elements.Count == 0)
                return LayoutOutput.Failure("No elements to calculate");

            var outputs = new List<ElementOutput>();
            foreach (var element in definition.Elements)
            {
                var output = CalculateElement(element, input);
                outputs.Add(output);
            }
            return LayoutOutput.Success(outputs);
        }

        public ElementOutput CalculateElement(ElementDefinition element, LayoutInput input)
        {
            var pos = element.Position;
            var constraints = element.SizeConstraints;

            int w, h, x, y;

            if (pos.Width >= 0 && pos.Width <= 1)
                w = ProportionalMath.RatioToPixel(input.ParentWidth, pos.Width);
            else
                w = input.ParentWidth;

            if (pos.Height >= 0 && pos.Height <= 1)
                h = ProportionalMath.RatioToPixel(input.ParentHeight, pos.Height);
            else
                h = input.ParentHeight;

            w = Math.Max(constraints.MinWidth, Math.Min(constraints.MaxWidth, w > 0 ? w : input.ParentWidth));
            h = Math.Max(constraints.MinHeight, Math.Min(constraints.MaxHeight, h > 0 ? h : input.ParentHeight));

            if (pos.Left >= 0 && pos.Left <= 1)
            {
                x = ProportionalMath.RatioToPixel(input.ParentWidth, pos.Left);
            }
            else if (pos.Left < 0)
            {
                double rightRatio = -pos.Left;
                int rightPx = ProportionalMath.RatioToPixel(input.ParentWidth, rightRatio);
                x = input.ParentWidth - rightPx - w;
            }
            else
            {
                x = input.X;
            }

            if (pos.Top >= 0 && pos.Top <= 1)
            {
                y = ProportionalMath.RatioToPixel(input.ParentHeight, pos.Top);
            }
            else if (pos.Top < 0)
            {
                double bottomRatio = -pos.Top;
                int bottomPx = ProportionalMath.RatioToPixel(input.ParentHeight, bottomRatio);
                y = input.ParentHeight - bottomPx - h;
            }
            else
            {
                y = input.Y;
            }

            return new ElementOutput
            {
                ControlName = element.ControlName,
                X = x, Y = y, Width = w, Height = h,
                ZOrder = element.ZOrder.Order
            };
        }
    }

    public class StackLayoutStrategy : ILayoutStrategy
    {
        public string Name => "Stack";
        public LayoutType LayoutType => LayoutType.Stack;

        public LayoutOutput Calculate(LayoutDefinition definition, LayoutInput input)
        {
            if (definition.Elements.Count == 0)
                return LayoutOutput.Failure("No elements to calculate");

            var stackConfig = definition.Constraints?.Tag as StackLayoutConfig
                ?? new StackLayoutConfig
                {
                    LayoutType = StackLayoutType.VerticalStack,
                    Spacing = 5,
                    Padding = new PaddingConfig(0),
                    StretchWidth = true
                };

            var items = StackLayoutMath.Calculate(
                input.ParentWidth, input.ParentHeight,
                definition.Elements.Count, stackConfig);

            var outputs = new List<ElementOutput>();
            for (int i = 0; i < items.Count && i < definition.Elements.Count; i++)
            {
                var item = items[i];
                var elemDef = definition.Elements[i];
                outputs.Add(new ElementOutput
                {
                    ControlName = elemDef.ControlName,
                    X = item.X, Y = item.Y,
                    Width = item.Width, Height = item.Height,
                    ZOrder = elemDef.ZOrder.Order
                });
            }

            return LayoutOutput.Success(outputs);
        }
    }


    public class PushRepairStrategy
    {
        private readonly ILayoutLogger? _logger;

        public PushRepairStrategy(ILayoutLogger? logger = null)
        {
            _logger = logger;
        }

        public List<ElementOutput> Repair(List<ElementOutput> elements, int parentWidth, int parentHeight)
        {
            if (elements.Count < 2) return elements;

            int spacing = CalculateSpacing(parentWidth, parentHeight);
            int maxIterations = Math.Min(elements.Count * 3, 30);
            bool overlapped;
            int iteration = 0;

            var resolved = elements.Select(e => e with { }).ToList();

            do
            {
                overlapped = false;
                iteration++;

                for (int i = 0; i < resolved.Count; i++)
                {
                    for (int j = i + 1; j < resolved.Count; j++)
                    {
                        var e1 = resolved[i];
                        var e2 = resolved[j];

                        var r1 = new Rect(e1.X, e1.Y, e1.Width, e1.Height);
                        var r2 = new Rect(e2.X, e2.Y, e2.Width, e2.Height);

                        if (!OverlapMath.WouldOverlap(r1, r2))
                            continue;

                        overlapped = true;

                        bool moveE2 = e2.ZOrder >= e1.ZOrder;
                        var (fixedEl, movingEl) = moveE2 ? (e1, e2) : (e2, e1);

                        var overlapRect = OverlapMath.CalculateOverlapRect(r1, r2);
                        bool moved = false;

                        if (overlapRect.Width <= overlapRect.Height)
                        {
                            if (fixedEl.X + fixedEl.Width + spacing + movingEl.Width <= parentWidth)
                            {
                                UpdatePosition(resolved, movingEl.ControlName, fixedEl.X + fixedEl.Width + spacing, movingEl.Y);
                                moved = true;
                            }
                            else if (movingEl.X - spacing - movingEl.Width >= 0)
                            {
                                UpdatePosition(resolved, movingEl.ControlName, movingEl.X - spacing - movingEl.Width, movingEl.Y);
                                moved = true;
                            }
                        }

                        if (!moved)
                        {
                            if (fixedEl.Y + fixedEl.Height + spacing + movingEl.Height <= parentHeight)
                            {
                                UpdatePosition(resolved, movingEl.ControlName, movingEl.X, fixedEl.Y + fixedEl.Height + spacing);
                                moved = true;
                            }
                            else if (movingEl.Y - spacing - movingEl.Height >= 0)
                            {
                                UpdatePosition(resolved, movingEl.ControlName, movingEl.X, movingEl.Y - spacing - movingEl.Height);
                                moved = true;
                            }
                        }

                        if (!moved)
                        {
                            int shrinkW = Math.Max(20, movingEl.Width - overlapRect.Width / 2);
                            int shrinkH = Math.Max(20, movingEl.Height - overlapRect.Height / 2);
                            UpdateSize(resolved, movingEl.ControlName, shrinkW, shrinkH);
                        }
                    }
                }
            } while (overlapped && iteration < maxIterations);

            return resolved;
        }

        private static void UpdatePosition(List<ElementOutput> resolved, string controlName, int newX, int newY)
        {
            for (int k = 0; k < resolved.Count; k++)
            {
                if (resolved[k].ControlName == controlName)
                {
                    resolved[k] = resolved[k] with { X = newX, Y = newY };
                    break;
                }
            }
        }

        private static void UpdateSize(List<ElementOutput> resolved, string controlName, int newWidth, int newHeight)
        {
            for (int k = 0; k < resolved.Count; k++)
            {
                if (resolved[k].ControlName == controlName)
                {
                    resolved[k] = resolved[k] with { Width = newWidth, Height = newHeight };
                    break;
                }
            }
        }

        private static int CalculateSpacing(int parentWidth, int parentHeight)
        {
            if (parentWidth <= 0 || parentHeight <= 0) return 5;
            double diagonal = Math.Sqrt((double)parentWidth * parentWidth + (double)parentHeight * parentHeight);
            const double referenceDiagonal = 2203.0;
            double ratio = diagonal / referenceDiagonal;
            int spacing = (int)Math.Round(5 * ratio);
            return spacing < 3 ? 3 : (spacing > 20 ? 20 : spacing);
        }
    }


    public class LayoutEngine
    {
        private readonly Dictionary<LayoutType, ILayoutStrategy> _strategies = new();
        private readonly ILayoutValidator _validator;
        private readonly PushRepairStrategy _repairStrategy;
        private readonly ILayoutLogger? _logger;
        private readonly ConcurrentDictionary<string, SmartLayoutConfig> _attached = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTokens = new();
        private readonly ConcurrentDictionary<string, bool> _pendingContainers = new();
        private readonly ConcurrentDictionary<string, IResizeNotifiable> _subscribedNotifiables = new();

        private class ElementContainerAdapter : ILayoutContainer
        {
            private readonly ILayoutElement _element;
            public ElementContainerAdapter(ILayoutElement element) => _element = element;
            public string Name => _element.Name;
            public int Width => _element.Width;
            public int Height => _element.Height;
            public object? Tag { get => _element.Tag; set => _element.Tag = value; }
            public IEnumerable<ILayoutElement> GetChildren() => _element.GetChildren();
            public ILayoutElement? FindChild(string name)
                => _element.GetChildren().FirstOrDefault(c => c.Name == name);
        }

        internal LayoutEngine(
            IEnumerable<ILayoutStrategy> strategies,
            ILayoutValidator validator,
            PushRepairStrategy repairStrategy,
            ILayoutLogger? logger)
        {
            foreach (var s in strategies)
                _strategies[s.LayoutType] = s;
            // 必填端口（T）：校验器/修复策略由装配点（LayoutEngineBuilder）显式提供；禁 ?? new 默认冒充装配。
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _repairStrategy = repairStrategy ?? throw new ArgumentNullException(nameof(repairStrategy));
            _logger = logger;
        }

        public void RegisterStrategy(ILayoutStrategy strategy)
        {
            _strategies[strategy.LayoutType] = strategy;
        }

        public void Apply(ILayoutContainer container, object? config = null)
        {
            var smartConfig = config as SmartLayoutConfig ?? new SmartLayoutConfig { LogEnabled = _logger != null };

            if (container.Width <= 0 || container.Height <= 0)
            {
                _logger?.Log(LogLevel.Warning, $"Container '{container.Name}' has invalid size ({container.Width}x{container.Height}), deferring");

                if (!_pendingContainers.TryGetValue(container.Name, out _))
                {
                    _pendingContainers[container.Name] = true;

                    if (container is IResizeNotifiable notifiable)
                    {
                        _subscribedNotifiables[container.Name] = notifiable;
                        notifiable.SubscribeSizeChanged(() =>
                        {
                            _pendingContainers.TryRemove(container.Name, out _);
                            _logger?.Log(LogLevel.Information, $"Container '{container.Name}' size now valid, applying deferred layout");
                            ApplyInternal(container, smartConfig);
                        });
                    }
                }
                return;
            }

            ApplyInternal(container, smartConfig);
        }

        public void Refresh(ILayoutContainer container)
        {
            if (_attached.TryGetValue(container.Name, out var config))
                Apply(container, config);
        }

        public void Attach(ILayoutContainer container, object? config = null)
        {
            var smartConfig = config as SmartLayoutConfig ?? new SmartLayoutConfig();
            _attached[container.Name] = smartConfig;
            Apply(container, smartConfig);

            if (smartConfig.EnableResizeHandling)
            {
                _logger?.Log(LogLevel.Information, $"LayoutEngine attached to '{container.Name}' with resize handling");
            }
        }

        public void Detach(ILayoutContainer container)
        {
            _attached.TryRemove(container.Name, out _);
            if (_debounceTokens.TryRemove(container.Name, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
            if (_subscribedNotifiables.TryRemove(container.Name, out var notifiable))
            {
                notifiable.UnsubscribeSizeChanged();
            }
        }

        public void NotifyResize(ILayoutContainer container)
        {
            if (!_attached.TryGetValue(container.Name, out var config)) return;

            if (_debounceTokens.TryRemove(container.Name, out var oldCts))
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }

            var cts = new CancellationTokenSource();
            _debounceTokens[container.Name] = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(config.DebounceInterval, cts.Token);
                    if (!cts.Token.IsCancellationRequested)
                        Apply(container, config);
                }
                catch (TaskCanceledException) { }
            });
        }


        private void ApplyInternal(ILayoutContainer container, SmartLayoutConfig smartConfig)
        {
            var layoutDef = container.Tag as LayoutDefinition;
            if (layoutDef == null)
            {
                layoutDef = BuildDefinitionFromChildren(container, smartConfig);
                if (layoutDef == null) return;
            }

            if (!_validator.ValidateDefinition(layoutDef, out var errors))
            {
                _logger?.Log(LogLevel.Warning, $"Layout validation failed for '{container.Name}': {string.Join(", ", errors)}");
                return;
            }

            if (!_strategies.TryGetValue(layoutDef.Type, out var strategy))
            {
                _logger?.Log(LogLevel.Warning, $"No strategy registered for layout type '{layoutDef.Type}'");
                return;
            }

            var input = new LayoutInput(0, 0, container.Width, container.Height,
                                        container.Width, container.Height);
            var output = strategy.Calculate(layoutDef, input);

            if (output.IsValid && output.Elements != null)
            {
                if (smartConfig.LogEnabled)
                    _logger?.Log(LogLevel.Information, $"Applying layout to {output.Elements.Count} elements in '{container.Name}'");

                ApplyOutput(container, output, smartConfig);
            }
        }

        private LayoutDefinition? BuildDefinitionFromChildren(ILayoutContainer container, SmartLayoutConfig config)
        {
            var children = container.GetChildren().ToList();
            if (children.Count == 0) return null;

            var def = new LayoutDefinition
            {
                Name = container.Name,
                Type = LayoutType.Proportional,
                Elements = new List<ElementDefinition>()
            };

            foreach (var child in children)
            {
                var elementDef = child.Tag as ElementDefinition;
                if (elementDef != null)
                {
                    def.Elements.Add(elementDef);
                }
                else
                {
                    def.Elements.Add(new ElementDefinition
                    {
                        ControlName = child.Name,
                        Position = new LayoutPosition { Left = -1, Top = -1, Width = -1, Height = -1 }
                    });
                }
            }

            container.Tag = def;
            return def;
        }

        private void ApplyOutput(ILayoutContainer container, LayoutOutput output, SmartLayoutConfig config)
        {
            if (output.Elements == null) return;

            var resolvedElements = config.OverlapResolver != null
                ? config.OverlapResolver(output.Elements.ToList(), container.Width, container.Height)
                : _repairStrategy.Repair(output.Elements.ToList(), container.Width, container.Height);

            foreach (var element in resolvedElements.OrderBy(e => e.ZOrder))
            {
                var child = container.FindChild(element.ControlName);
                if (child != null)
                {
                    child.SetBounds(element.X, element.Y, element.Width, element.Height);
                    if (config.LogEnabled)
                        _logger?.Log(LogLevel.Information, $"  {element.ControlName}: ({element.X},{element.Y} {element.Width}x{element.Height})");

                    if (config.Recursive && child.HasChildren)
                    {
                        var childContainer = new ElementContainerAdapter(child);
                        Apply(childContainer, config);
                    }
                }
            }
        }
    }


    public class LayoutEngineBuilder
    {
        private readonly List<ILayoutStrategy> _strategies = new();
        private ILayoutValidator _validator = new DefaultLayoutValidator();
        private PushRepairStrategy? _repairStrategy;
        private ILayoutLogger? _logger;

        public LayoutEngineBuilder UseProportionalLayout()
        {
            _strategies.Add(new ProportionalLayoutStrategy());
            return this;
        }

        public LayoutEngineBuilder UseStackLayout()
        {
            _strategies.Add(new StackLayoutStrategy());
            return this;
        }

        public LayoutEngineBuilder UseStrategy(ILayoutStrategy strategy)
        {
            _strategies.Add(strategy);
            return this;
        }

        public LayoutEngineBuilder UseValidator(ILayoutValidator validator)
        {
            _validator = validator;
            return this;
        }

        public LayoutEngineBuilder UsePushRepair()
        {
            _repairStrategy = new PushRepairStrategy(_logger);
            return this;
        }

        public LayoutEngineBuilder UseRepairStrategy(PushRepairStrategy repairStrategy)
        {
            _repairStrategy = repairStrategy;
            return this;
        }

        public LayoutEngineBuilder WithLogger(ILayoutLogger? logger)
        {
            _logger = logger;
            return this;
        }

        public LayoutEngine Build()
        {
            if (_strategies.Count == 0)
                _strategies.Add(new ProportionalLayoutStrategy());

            // 组合根：builder 即 LayoutEngine 的装配点；未显式配置的修复策略在此补默认实现（引擎构造端口为必填 T）。
            var repairStrategy = _repairStrategy;
            if (repairStrategy is null)
                repairStrategy = new PushRepairStrategy(_logger);

            return new LayoutEngine(_strategies, _validator, repairStrategy, _logger);
        }
    }


    public class DefaultLayoutValidator : ILayoutValidator
    {
        public bool ValidateDefinition(LayoutDefinition definition, out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(definition.Name))
                errors.Add("Layout name cannot be empty");

            if (definition.Elements.Count == 0)
                errors.Add("Layout must contain at least one element");

            foreach (var element in definition.Elements)
            {
                if (!ValidateElement(element, out var elemErrors))
                    errors.AddRange(elemErrors);
            }

            return errors.Count == 0;
        }

        public bool ValidateElement(ElementDefinition element, out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(element.ControlName))
                errors.Add("Element control name cannot be empty");

            var pos = element.Position;
            if (pos.Left > 1 || pos.Top > 1 || pos.Width > 1 || pos.Height > 1)
                errors.Add($"Position values for '{element.ControlName}' must be <= 1 (use [-1, 1] range, where <0 means right/bottom offset)");

            return errors.Count == 0;
        }
    }
}
