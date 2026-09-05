#region USINGS
using System;
#endregion

namespace MathematicalAlgorithms
{
    public record TensorShape
    {
        public int[] Dimensions { get; }

        public int Rank => Dimensions.Length;

        public long TotalElements
        {
            get
            {
                long total = 1;
                for (int i = 0; i < Dimensions.Length; i++)
                    total *= Dimensions[i];
                return total;
            }
        }

        public long EstimatedMemoryBytes => TotalElements * sizeof(double);

        public TensorShape(params int[] dimensions)
        {
            if (dimensions == null || dimensions.Length == 0)
                throw new ArgumentException("At least one dimension is required", nameof(dimensions));

            Dimensions = new int[dimensions.Length];
            Array.Copy(dimensions, Dimensions, dimensions.Length);
        }

        public int this[int index]
        {
            get
            {
                if (index < 0) index += Dimensions.Length;
                if (index < 0 || index >= Dimensions.Length)
                    throw new IndexOutOfRangeException($"Dimension index {index} out of range [0, {Dimensions.Length})");
                return Dimensions[index];
            }
        }

        public static bool CanBroadcast(TensorShape a, TensorShape b)
        {
            if (a == null || b == null) return false;

            int maxDims = Math.Max(a.Rank, b.Rank);
            for (int i = 0; i < maxDims; i++)
            {
                int dimA = i < a.Rank ? a.Dimensions[a.Rank - 1 - i] : 1;
                int dimB = i < b.Rank ? b.Dimensions[b.Rank - 1 - i] : 1;

                if (dimA != dimB && dimA != 1 && dimB != 1)
                    return false;
            }
            return true;
        }

        public static TensorShape Broadcast(TensorShape a, TensorShape b)
        {
            if (!CanBroadcast(a, b))
                throw new InvalidOperationException(
                    $"Shapes {a} and {b} cannot be broadcast together");

            int maxDims = Math.Max(a.Rank, b.Rank);
            var result = new int[maxDims];

            for (int i = 0; i < maxDims; i++)
            {
                int dimA = i < a.Rank ? a.Dimensions[a.Rank - 1 - i] : 1;
                int dimB = i < b.Rank ? b.Dimensions[b.Rank - 1 - i] : 1;
                result[maxDims - 1 - i] = Math.Max(dimA, dimB);
            }

            return new TensorShape(result);
        }

        public void ValidateIndex(params int[] indices)
        {
            if (indices == null || indices.Length != Dimensions.Length)
                throw new ArgumentException(
                    $"Expected {Dimensions.Length} indices, got {(indices?.Length ?? 0)}",
                    nameof(indices));

            for (int i = 0; i < Dimensions.Length; i++)
            {
                if (indices[i] < 0 || indices[i] >= Dimensions[i])
                    throw new IndexOutOfRangeException(
                        $"Index [{string.Join(", ", indices)}] out of range for shape {this}");
            }
        }

        public TensorShape Slice(int dim, int start, int length)
        {
            if (dim < 0 || dim >= Dimensions.Length)
                throw new ArgumentException($"Dimension {dim} out of range [0, {Dimensions.Length})", nameof(dim));
            if (start < 0 || start + length > Dimensions[dim])
                throw new ArgumentException(
                    $"Slice [{start}, {start + length}) out of range [0, {Dimensions[dim]})", nameof(start));

            var result = new int[Dimensions.Length];
            Array.Copy(Dimensions, result, Dimensions.Length);
            result[dim] = length;
            return new TensorShape(result);
        }

        public override string ToString() => $"[{string.Join("×", Dimensions)}]";
    }
}
