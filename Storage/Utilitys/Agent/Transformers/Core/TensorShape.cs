#region USINGS
using System;
using MathematicalAlgorithms;
#endregion

namespace Agent.Transformers.Core
{
    public record TensorShape3D(int Batch, int SeqLen, int Dim)
    {
        public long TotalElements => (long)Batch * SeqLen * Dim;

        public long EstimatedMemoryBytes => TotalElements * sizeof(double);

        public static bool CanBroadcast(TensorShape3D a, TensorShape3D b)
        {
            return BroadcastHelper.CanBroadcast(
                new[] { a.Batch, a.SeqLen, a.Dim },
                new[] { b.Batch, b.SeqLen, b.Dim });
        }

        public static TensorShape3D Broadcast(TensorShape3D a, TensorShape3D b)
        {
            var result = BroadcastHelper.GetBroadcastShape(
                new[] { a.Batch, a.SeqLen, a.Dim },
                new[] { b.Batch, b.SeqLen, b.Dim });

            return new TensorShape3D(result[0], result[1], result[2]);
        }

        public void ValidateIndex(int b, int s, int d)
        {
            if (b < 0 || b >= Batch)
                throw new IndexOutOfRangeException(
                    $"Batch index {b} out of range [0, {Batch})");
            if (s < 0 || s >= SeqLen)
                throw new IndexOutOfRangeException(
                    $"SeqLen index {s} out of range [0, {SeqLen})");
            if (d < 0 || d >= Dim)
                throw new IndexOutOfRangeException(
                    $"Dim index {d} out of range [0, {Dim})");
        }

        public override string ToString() => $"[{Batch}×{SeqLen}×{Dim}]";
    }
}
