#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalLayoutMath
{

    public class FixScore
    {
        public double Cost { get; set; } = double.MaxValue;
        public double Displacement { get; set; }
        public double RemainingOverlapRatio { get; set; }
        public double AspectRatioChange { get; set; }
        public bool IsInBounds { get; set; }
        public string Description { get; set; } = string.Empty;
        public int SuggestedX { get; set; }
        public int SuggestedY { get; set; }
        public int SuggestedWidth { get; set; }
        public int SuggestedHeight { get; set; }

        public bool IsValid => IsInBounds && Cost < double.MaxValue;
    }


    public class MultiObjectiveOptimizer
    {
        private double _wDisplacement = 0.35;
        private double _wRemaining = 0.40;
        private double _wAspect = 0.15;
        private double _wBounds = 0.10;

        private readonly int _parentWidth;
        private readonly int _parentHeight;
        private readonly int _margin;

        public double WeightDisplacement
        {
            get => _wDisplacement;
            set => _wDisplacement = ClampWeight(value);
        }

        public double WeightRemainingOverlap
        {
            get => _wRemaining;
            set => _wRemaining = ClampWeight(value);
        }

        public double WeightAspectRatio
        {
            get => _wAspect;
            set => _wAspect = ClampWeight(value);
        }

        public double WeightBounds
        {
            get => _wBounds;
            set => _wBounds = ClampWeight(value);
        }

        public int Margin => _margin;

        public MultiObjectiveOptimizer(int parentWidth, int parentHeight, int margin = -1)
        {
            _parentWidth = parentWidth;
            _parentHeight = parentHeight;
            _margin = margin >= 0
                ? margin
                : SmartSpacingCalculator.CalculateBaseSpacing(parentWidth, parentHeight);
        }


        public FixScore? SelectBestStrategy(
            OverlapPair overlap,
            List<OverlapMath.ElementDesc> allElements)
        {
            var strategies = new List<FixScore>();

            var targetName = overlap.ElementB;

            strategies.Add(EvaluateShiftRight(targetName, overlap, allElements));
            strategies.Add(EvaluateShiftDown(targetName, overlap, allElements));
            strategies.Add(EvaluateShiftLeft(targetName, overlap, allElements));
            strategies.Add(EvaluateShiftUp(targetName, overlap, allElements));
            strategies.Add(EvaluateShrink(targetName, overlap, allElements));
            strategies.Add(EvaluateSwapPosition(targetName, overlap.ElementA, overlap, allElements));

            return strategies
                .Where(s => s.IsValid)
                .OrderBy(s => s.Cost)
                .FirstOrDefault();
        }

        public void SetWeights(double displacement, double remaining, double aspect, double bounds)
        {
            _wDisplacement = ClampWeight(displacement);
            _wRemaining = ClampWeight(remaining);
            _wAspect = ClampWeight(aspect);
            _wBounds = ClampWeight(bounds);
        }


        private FixScore EvaluateShiftRight(
            string targetName,
            OverlapPair overlap, List<OverlapMath.ElementDesc> allElements)
        {
            int newX = overlap.RectA.Right + _margin;
            int newY = overlap.RectB.Y;
            int newW = overlap.RectB.Width;
            int newH = overlap.RectB.Height;

            bool inBounds = newX + newW <= _parentWidth;
            double displacement = Math.Abs(newX - overlap.RectB.X);

            var newRect = new Rect(newX, newY, newW, newH);
            double remainingOverlap = CalculateMaxOverlapWithOthers(targetName, newRect, allElements);

            double cost = CalculateCost(displacement, remainingOverlap,
                overlap.RectB.Width, overlap.RectB.Height, newW, newH, inBounds);

            return new FixScore
            {
                Cost = cost,
                Displacement = displacement,
                RemainingOverlapRatio = remainingOverlap,
                IsInBounds = inBounds,
                Description = $"向右偏移至 X={newX}",
                SuggestedX = newX,
                SuggestedY = newY,
                SuggestedWidth = newW,
                SuggestedHeight = newH
            };
        }

        private FixScore EvaluateShiftDown(
            string targetName,
            OverlapPair overlap, List<OverlapMath.ElementDesc> allElements)
        {
            int newX = overlap.RectB.X;
            int newY = overlap.RectA.Bottom + _margin;
            int newW = overlap.RectB.Width;
            int newH = overlap.RectB.Height;

            bool inBounds = newY + newH <= _parentHeight;
            double displacement = Math.Abs(newY - overlap.RectB.Y);

            var newRect = new Rect(newX, newY, newW, newH);
            double remainingOverlap = CalculateMaxOverlapWithOthers(targetName, newRect, allElements);

            double cost = CalculateCost(displacement, remainingOverlap,
                overlap.RectB.Width, overlap.RectB.Height, newW, newH, inBounds);

            return new FixScore
            {
                Cost = cost,
                Displacement = displacement,
                RemainingOverlapRatio = remainingOverlap,
                IsInBounds = inBounds,
                Description = $"向下偏移至 Y={newY}",
                SuggestedX = newX,
                SuggestedY = newY,
                SuggestedWidth = newW,
                SuggestedHeight = newH
            };
        }

        private FixScore EvaluateShiftLeft(
            string targetName,
            OverlapPair overlap, List<OverlapMath.ElementDesc> allElements)
        {
            int newX = overlap.RectA.Left - overlap.RectB.Width - _margin;
            int newY = overlap.RectB.Y;
            int newW = overlap.RectB.Width;
            int newH = overlap.RectB.Height;

            bool inBounds = newX >= 0;
            double displacement = Math.Abs(newX - overlap.RectB.X);

            var newRect = new Rect(newX, newY, newW, newH);
            double remainingOverlap = CalculateMaxOverlapWithOthers(targetName, newRect, allElements);

            double cost = CalculateCost(displacement, remainingOverlap,
                overlap.RectB.Width, overlap.RectB.Height, newW, newH, inBounds);

            return new FixScore
            {
                Cost = cost,
                Displacement = displacement,
                RemainingOverlapRatio = remainingOverlap,
                IsInBounds = inBounds,
                Description = $"向左偏移至 X={newX}",
                SuggestedX = newX,
                SuggestedY = newY,
                SuggestedWidth = newW,
                SuggestedHeight = newH
            };
        }

        private FixScore EvaluateShiftUp(
            string targetName,
            OverlapPair overlap, List<OverlapMath.ElementDesc> allElements)
        {
            int newX = overlap.RectB.X;
            int newY = overlap.RectA.Top - overlap.RectB.Height - _margin;
            int newW = overlap.RectB.Width;
            int newH = overlap.RectB.Height;

            bool inBounds = newY >= 0;
            double displacement = Math.Abs(newY - overlap.RectB.Y);

            var newRect = new Rect(newX, newY, newW, newH);
            double remainingOverlap = CalculateMaxOverlapWithOthers(targetName, newRect, allElements);

            double cost = CalculateCost(displacement, remainingOverlap,
                overlap.RectB.Width, overlap.RectB.Height, newW, newH, inBounds);

            return new FixScore
            {
                Cost = cost,
                Displacement = displacement,
                RemainingOverlapRatio = remainingOverlap,
                IsInBounds = inBounds,
                Description = $"向上偏移至 Y={newY}",
                SuggestedX = newX,
                SuggestedY = newY,
                SuggestedWidth = newW,
                SuggestedHeight = newH
            };
        }

        private FixScore EvaluateShrink(
            string targetName,
            OverlapPair overlap, List<OverlapMath.ElementDesc> allElements)
        {
            int overlapW = overlap.OverlapRect.Width;
            int overlapH = overlap.OverlapRect.Height;

            int minSize = SmartSpacingCalculator.CalculateMinElementSize(
                Math.Min(_parentWidth, _parentHeight));

            int newW = Math.Max(minSize, overlap.RectB.Width - overlapW - _margin);
            int newH = overlap.RectB.Height;
            int newX = overlap.RectB.X;
            int newY = overlap.RectB.Y;

            var newRect = new Rect(newX, newY, newW, newH);
            if (OverlapMath.WouldOverlap(newRect, overlap.RectA))
            {
                newW = overlap.RectB.Width;
                newH = Math.Max(minSize, overlap.RectB.Height - overlapH - _margin);
                newRect = new Rect(newX, newY, newW, newH);
            }


            bool inBounds = true;
            double displacement = 0;
            double aspectChange = Math.Abs(
                (double)overlap.RectB.Width / overlap.RectB.Height -
                (double)newW / newH);

            double remainingOverlap = CalculateMaxOverlapWithOthers(targetName, newRect, allElements);

            double cost = CalculateCost(displacement, remainingOverlap,
                overlap.RectB.Width, overlap.RectB.Height, newW, newH, inBounds);

            return new FixScore
            {
                Cost = cost,
                Displacement = displacement,
                RemainingOverlapRatio = remainingOverlap,
                AspectRatioChange = aspectChange,
                IsInBounds = inBounds,
                Description = $"缩小至 {newW}x{newH}",
                SuggestedX = newX,
                SuggestedY = newY,
                SuggestedWidth = newW,
                SuggestedHeight = newH
            };
        }

        private FixScore EvaluateSwapPosition(
            string targetName, string anchorName,
            OverlapPair overlap, List<OverlapMath.ElementDesc> allElements)
        {
            int newX = overlap.RectA.X;
            int newY = overlap.RectA.Y;
            int newW = overlap.RectB.Width;
            int newH = overlap.RectB.Height;

            bool inBounds = newX + newW <= _parentWidth && newY + newH <= _parentHeight;
            double displacement = Math.Sqrt(
                Math.Pow(newX - overlap.RectB.X, 2) +
                Math.Pow(newY - overlap.RectB.Y, 2));

            var newRect = new Rect(newX, newY, newW, newH);
            double remainingOverlap = CalculateMaxOverlapWithOthers(targetName, newRect, allElements);

            double cost = CalculateCost(displacement, remainingOverlap,
                overlap.RectB.Width, overlap.RectB.Height, newW, newH, inBounds);

            return new FixScore
            {
                Cost = cost,
                Displacement = displacement,
                RemainingOverlapRatio = remainingOverlap,
                IsInBounds = inBounds,
                Description = $"与 '{anchorName}' 交换位置",
                SuggestedX = newX,
                SuggestedY = newY,
                SuggestedWidth = newW,
                SuggestedHeight = newH
            };
        }


        private double CalculateMaxOverlapWithOthers(
            string targetName, Rect newRect,
            List<OverlapMath.ElementDesc> allElements)
        {
            double maxRatio = 0;

            foreach (var other in allElements)
            {
                if (other.Name == targetName) continue;

                if (!OverlapMath.WouldOverlap(newRect, other.Rect)) continue;

                var overlapRect = OverlapMath.CalculateOverlapRect(newRect, other.Rect);
                var ratio = OverlapMath.CalculateOverlapRatio(newRect, other.Rect, overlapRect);
                maxRatio = Math.Max(maxRatio, ratio);
            }

            return maxRatio;
        }

        private double CalculateCost(
            double displacement, double remainingOverlap,
            int origW, int origH, int newW, int newH,
            bool inBounds)
        {
            double diagonal = Math.Sqrt(_parentWidth * _parentWidth + _parentHeight * _parentHeight);
            double normDisplacement = diagonal > 0 ? displacement / diagonal : 0;

            double origRatio = origH > 0 ? (double)origW / origH : 1;
            double newRatio = newH > 0 ? (double)newW / newH : 1;
            double aspectChange = origRatio > 0 ? Math.Abs(newRatio - origRatio) / origRatio : 0;

            double cost =
                _wDisplacement * normDisplacement +
                _wRemaining * remainingOverlap +
                _wAspect * Math.Min(aspectChange, 1.0) +
                _wBounds * (inBounds ? 0 : 1.0);

            return cost;
        }

        private static double ClampWeight(double value)
        {
            return Math.Max(0, Math.Min(1, value));
        }
    }
}
