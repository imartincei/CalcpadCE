using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

namespace Calcpad.Core
{
    internal static class HpMatmulWinograd
    {
        private const int KernelSize = 64;
        private const int ParallelThreshold = 256;
        private static readonly int VecSize = Vector<double>.Count;
        private static readonly ArrayPool<double> Pool = ArrayPool<double>.Shared;

        private readonly struct MatrixView
        {
            private readonly double[] _data;
            private readonly int _offset;
            internal readonly int Rows;
            internal readonly int Cols;
            internal readonly int Stride;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public MatrixView(double[] data, int n) : this(data, n, n, n, 0) { }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal MatrixView(double[] data, int rows, int cols, int stride, int offset)
            {
                _data = data;
                Rows = rows;
                Cols = cols;
                Stride = stride;
                _offset = offset;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Span<double> GetRow(int row) => _data.AsSpan(_offset + row * Stride, Cols);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref double GetRowReference(int row) => ref _data[_offset + row * Stride];

            internal double this[int row, int col]
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _data[_offset + row * Stride + col];
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                set => _data[_offset + row * Stride + col] = value;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal MatrixView Slice(int rowStart, int colStart, int rows, int cols) =>
                new(_data, rows, cols, Stride, _offset + rowStart * Stride + colStart);
        }

        internal static HpMatrix Multiply(HpMatrix A, HpMatrix B)
        {
            var n = A.RowCount;
            var m = NextPowerOfTwo(n);
            var l = m * m;
            var AFlat = Pool.Rent(l);
            var BFlat = Pool.Rent(l);
            var CFlat = Pool.Rent(l);
            try
            {
                AFlat.AsSpan().Clear();
                BFlat.AsSpan().Clear();
                CFlat.AsSpan().Clear();
                CopyToFlat(A, AFlat, n, m);
                CopyToFlat(B, BFlat, n, m);
                var viewA = new MatrixView(AFlat, m);
                var viewB = new MatrixView(BFlat, m);
                var viewC = new MatrixView(CFlat, m);
                WinogradRecursive(viewA, viewB, viewC, 0);
                Unit unit = Unit.Multiply(A.Units, B.Units, out var d, true);
                Vectorized.Scale(CFlat, d);
                return HpMatrix.FromFlatArray(CFlat, unit, n, m);
            }
            finally
            {
                Pool.Return(AFlat);
                Pool.Return(BFlat);
                Pool.Return(CFlat);
            }
        }
        private static void CopyToFlat(HpMatrix src, double[] dst, int n, int stride)
        {
            var rows = src.HpRows;
            var ii = 0;
            if (src.Type == Matrix.MatrixType.Full || src.Type == Matrix.MatrixType.LowerTriangular)
                for (int i = 0; i < n; ++i)
                {
                    var row = rows[i];
                    Array.Copy(row.Raw, 0, dst, ii, row.Size);
                    ii += stride;
                }
            else
                for (int i = 0; i < n; ++i)
                {
                    for (int j = 0; j < n; ++j)
                        dst[ii + j] = src.GetValue(i, j);

                    ii += stride;
                }
        }

        private static int NextPowerOfTwo(int n)
        {
            var power = 1;
            while (power < n)
                power *= 2;

            return power;
        }

        private static void WinogradRecursive(MatrixView A, MatrixView B, MatrixView C, int level)
        {
            var n = A.Rows;
            if (n == KernelSize)
            {
                if (Avx512F.IsSupported)
                    MultiplyAvx512Kernel_64x64(A, B, C);
                else
                    MultiplyFmaKernel_64x64(A, B, C);
                return;
            }
            if (n < KernelSize)
            {
                MultiplySimd(A, B, C);
                return;
            }
            var half = n / 2;
            var l = half * half;
            // Zero-copy quadrant slicing!
            var A11 = A.Slice(0, 0, half, half);
            var A12 = A.Slice(0, half, half, half);
            var A21 = A.Slice(half, 0, half, half);
            var A22 = A.Slice(half, half, half, half);
            var B11 = B.Slice(0, 0, half, half);
            var B12 = B.Slice(0, half, half, half);
            var B21 = B.Slice(half, 0, half, half);
            var B22 = B.Slice(half, half, half, half);
            var C11 = C.Slice(0, 0, half, half);
            var C12 = C.Slice(0, half, half, half);
            var C21 = C.Slice(half, 0, half, half);
            var C22 = C.Slice(half, half, half, half);
            // Rent temporaries
            var a22ModArr = Pool.Rent(l);
            var b22ModArr = Pool.Rent(l);
            var t1Arr = Pool.Rent(l);
            var t2Arr = Pool.Rent(l);
            var t3Arr = Pool.Rent(l);
            var t4Arr = Pool.Rent(l);
            var t5Arr = Pool.Rent(l);
            var t6Arr = Pool.Rent(l);
            var m1Arr = Pool.Rent(l);
            var m2Arr = Pool.Rent(l);
            var m3Arr = Pool.Rent(l);
            var m4Arr = Pool.Rent(l);
            var m5Arr = Pool.Rent(l);
            var m6Arr = Pool.Rent(l);
            var m7Arr = Pool.Rent(l);
            try
            {
                // Create views for temporaries
                var A22Mod = new MatrixView(a22ModArr, half);
                var B22Mod = new MatrixView(b22ModArr, half);
                var M1 = new MatrixView(m1Arr, half);
                var M2 = new MatrixView(m2Arr, half);
                var M3 = new MatrixView(m3Arr, half);
                var M4 = new MatrixView(m4Arr, half);
                var M5 = new MatrixView(m5Arr, half);
                var M6 = new MatrixView(m6Arr, half);
                var M7 = new MatrixView(m7Arr, half);
                // A22Mod = A12 - A21 + A22
                // B22Mod = B12 - B21 + B22
                AddSubAdd(A12, A21, A22, A22Mod);
                AddSubAdd(B12, B21, B22, B22Mod);
                int nextLevel = level + 1;
                // Parallel execution
                if (level == 0 && n > ParallelThreshold)
                {
                    Parallel.Invoke(
                        () => WinogradRecursive(A11, B11, M1, nextLevel),
                        () => WinogradRecursive(A12, B21, M2, nextLevel),
                        () =>
                        {
                            var T4 = new MatrixView(t4Arr, half);
                            Subtract(B22Mod, B11, T4);
                            WinogradRecursive(A21, T4, M3, nextLevel);
                        },
                        () => WinogradRecursive(A22Mod, B22Mod, M4, nextLevel),
                        () => {
                            var T1 = new MatrixView(t1Arr, half);
                            var T5 = new MatrixView(t5Arr, half);
                            Add(A21, A22Mod, T1);
                            Add(B21, B22Mod, T5);
                            WinogradRecursive(T1, T5, M5, nextLevel);
                        },
                        () =>
                        {
                            var T2 = new MatrixView(t2Arr, half);
                            var T6 = new MatrixView(t6Arr, half);
                            Subtract(A22Mod, A12, T2);
                            Subtract(B22Mod, B12, T6);
                            WinogradRecursive(T2, T6, M6, nextLevel);
                        },
                        () =>
                        {
                            var T3 = new MatrixView(t3Arr, half);
                            Subtract(A22Mod, A11, T3);
                            WinogradRecursive(T3, B12, M7, nextLevel);
                        }
                    );
                }
                else
                {
                    // Compute temporaries
                    var T1 = new MatrixView(t1Arr, half);
                    var T2 = new MatrixView(t2Arr, half);
                    var T3 = new MatrixView(t3Arr, half);
                    var T4 = new MatrixView(t4Arr, half);
                    var T5 = new MatrixView(t5Arr, half);
                    var T6 = new MatrixView(t6Arr, half);
                    Add(A21, A22Mod, T1);      // T1 = A21 + A22Mod
                    Subtract(A22Mod, A12, T2); // T2 = A22Mod - A12
                    Subtract(A22Mod, A11, T3); // T3 = A22Mod - A11
                    Subtract(B22Mod, B11, T4); // T4 = B22Mod - B11
                    Add(B21, B22Mod, T5);      // T5 = B21 + B22Mod
                    Subtract(B22Mod, B12, T6); // T6 = B22Mod - B12
                    // 7 recursive multiplications
                    WinogradRecursive(A11, B11, M1, nextLevel);
                    WinogradRecursive(A12, B21, M2, nextLevel);
                    WinogradRecursive(A21, T4, M3, nextLevel);
                    WinogradRecursive(A22Mod, B22Mod, M4, nextLevel);
                    WinogradRecursive(T1, T5, M5, nextLevel);
                    WinogradRecursive(T2, T6, M6, nextLevel);
                    WinogradRecursive(T3, B12, M7, nextLevel);
                }
                // Combine results
                Add(M1, M2, C11);                    // C11 = M1 + M2
                Compute_C22(M5, M6, M2, M4, C22);    // C22 = M5 + M6 - M2 - M4
                Subtract(M5, M7, C12);               // C12 = M5 - M7
                SubtractInPlace(C12, C22);           // C12 -= C22
                Add(M3, M6, C21);                    // C21 = M3 + M6
                SubtractFromInPlace(C22, C21);       // C21 = C22 - C21
            }
            finally
            {
                Pool.Return(a22ModArr);
                Pool.Return(b22ModArr);
                Pool.Return(t1Arr);
                Pool.Return(t2Arr);
                Pool.Return(t3Arr);
                Pool.Return(t4Arr);
                Pool.Return(t5Arr);
                Pool.Return(t6Arr);
                Pool.Return(m1Arr);
                Pool.Return(m2Arr);
                Pool.Return(m3Arr);
                Pool.Return(m4Arr);
                Pool.Return(m5Arr);
                Pool.Return(m6Arr);
                Pool.Return(m7Arr);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Add(MatrixView A, MatrixView B, MatrixView C)
        {
            var rows = A.Rows;
            var cols = A.Cols;
            for (int i = 0; i < rows; ++i)
            {
                var sA = A.GetRow(i);
                var sB = B.GetRow(i);
                var sC = C.GetRow(i);
                var vA = MemoryMarshal.Cast<double, Vector<double>>(sA);
                var vB = MemoryMarshal.Cast<double, Vector<double>>(sB);
                var vC = MemoryMarshal.Cast<double, Vector<double>>(sC);
                int vecLen = vA.Length;
                for (int j = 0; j < vecLen; ++j)
                    vC[j] = vA[j] + vB[j];

                for (int j = vecLen * VecSize; j < cols; ++j)
                    sC[j] = sA[j] + sB[j];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Subtract(MatrixView A, MatrixView B, MatrixView C)
        {
            var rows = A.Rows;
            var cols = A.Cols;
            for (int i = 0; i < rows; ++i)
            {
                var sA = A.GetRow(i);
                var sB = B.GetRow(i);
                var sC = C.GetRow(i);
                var vA = MemoryMarshal.Cast<double, Vector<double>>(sA);
                var vB = MemoryMarshal.Cast<double, Vector<double>>(sB);
                var vC = MemoryMarshal.Cast<double, Vector<double>>(sC);
                int vecLen = vA.Length;
                for (int j = 0; j < vecLen; ++j)
                    vC[j] = vA[j] - vB[j];

                for (int j = vecLen * VecSize; j < cols; ++j)
                    sC[j] = sA[j] - sB[j];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SubtractInPlace(MatrixView A, MatrixView B)
        {
            // A = A - B
            var rows = A.Rows;
            var cols = A.Cols;
            for (int i = 0; i < rows; ++i)
            {
                var sA = A.GetRow(i);
                var sB = B.GetRow(i);
                var vA = MemoryMarshal.Cast<double, Vector<double>>(sA);
                var vB = MemoryMarshal.Cast<double, Vector<double>>(sB);
                int vecLen = vA.Length;
                for (int j = 0; j < vecLen; ++j)
                    vA[j] -= vB[j];

                for (int j = vecLen * VecSize; j < cols; ++j)
                    sA[j] -= sB[j];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SubtractFromInPlace(MatrixView A, MatrixView B)
        {
            // B = A - B
            var rows = A.Rows;
            var cols = A.Cols;
            for (int i = 0; i < rows; ++i)
            {
                var sA = A.GetRow(i);
                var sB = B.GetRow(i);
                var vA = MemoryMarshal.Cast<double, Vector<double>>(sA);
                var vB = MemoryMarshal.Cast<double, Vector<double>>(sB);
                int vecLen = vA.Length;
                for (int j = 0; j < vecLen; ++j)
                    vB[j] = vA[j] - vB[j];

                for (int j = vecLen * VecSize; j < cols; ++j)
                    sB[j] = sA[j] - sB[j];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddSubAdd(MatrixView A, MatrixView B, MatrixView C, MatrixView result)
        {
            // result = A - B + C
            var rows = A.Rows;
            var cols = A.Cols;
            for (int i = 0; i < rows; ++i)
            {
                var sA = A.GetRow(i);
                var sB = B.GetRow(i);
                var sC = C.GetRow(i);
                var sR = result.GetRow(i);
                var vA = MemoryMarshal.Cast<double, Vector<double>>(sA);
                var vB = MemoryMarshal.Cast<double, Vector<double>>(sB);
                var vC = MemoryMarshal.Cast<double, Vector<double>>(sC);
                var vR = MemoryMarshal.Cast<double, Vector<double>>(sR);
                int vecLen = vA.Length;
                for (int j = 0; j < vecLen; ++j)
                    vR[j] = vA[j] - vB[j] + vC[j];

                for (int j = vecLen * VecSize; j < cols; ++j)
                    sR[j] = sA[j] - sB[j] + sC[j];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Compute_C22(MatrixView M5, MatrixView M6, MatrixView M2, MatrixView M4, MatrixView C22)
        {
            // C22 = M5 + M6 - M2 - M4
            var rows = M5.Rows;
            var cols = M5.Cols;
            for (int i = 0; i < rows; ++i)
            {
                var s2 = M2.GetRow(i);
                var s4 = M4.GetRow(i);
                var s5 = M5.GetRow(i);
                var s6 = M6.GetRow(i);
                var sC = C22.GetRow(i);
                var v2 = MemoryMarshal.Cast<double, Vector<double>>(s2);
                var v4 = MemoryMarshal.Cast<double, Vector<double>>(s4);
                var v5 = MemoryMarshal.Cast<double, Vector<double>>(s5);
                var v6 = MemoryMarshal.Cast<double, Vector<double>>(s6);
                var vC = MemoryMarshal.Cast<double, Vector<double>>(sC);
                int vecLen = v5.Length;
                for (int j = 0; j < vecLen; ++j)
                    vC[j] = v5[j] + v6[j] - v2[j] - v4[j];

                for (int j = vecLen * VecSize; j < cols; ++j)
                    sC[j] = s5[j] + s6[j] - s2[j] - s4[j];
            }
        }

        // Generic SIMD version
        private static void MultiplySimd(MatrixView A, MatrixView B, MatrixView C)
        {
            int n = A.Rows;
            for (int i = 0; i < n; ++i)
            {
                var sC = C.GetRow(i);
                sC.Clear();
                var vC = MemoryMarshal.Cast<double, Vector<double>>(sC);
                ref Vector<double> c = ref MemoryMarshal.GetReference(vC);
                for (int k = 0; k < n; ++k)
                {
                    var A_ik = A[i, k];
                    var vA = new Vector<double>(A_ik);
                    var sB = B.GetRow(k);
                    var vB = MemoryMarshal.Cast<double, Vector<double>>(sB);
                    ref Vector<double> b = ref MemoryMarshal.GetReference(vB);
                    var vecLen = vB.Length;

                    for (int j = 0; j < vecLen; ++j)
                        Unsafe.Add(ref c, j) += vA * Unsafe.Add(ref b, j);

                    for (int j = vecLen * VecSize; j < n; ++j)
                        sC[j] += A_ik * sB[j];
                }
            }
        }

        // Optimized 64x64 AVX-512 micro-kernel with register blocking
        // No loop unrolling here - we rely on the compiler to do that, and it does a good job with the independent FMA chains we create
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void MultiplyAvx512Kernel_64x64(MatrixView A, MatrixView B, MatrixView C)
        {
            const int VectorSize = 8;
            const int VectorsPerRow = KernelSize / VectorSize;

            // Allocate accumulator arrays outside the loop
            Span<Vector512<double>> vC0 = stackalloc Vector512<double>[VectorsPerRow];
            Span<Vector512<double>> vC1 = stackalloc Vector512<double>[VectorsPerRow];

            for (int i = 0; i < KernelSize; i += 2)
            {
                ref double rC0 = ref C.GetRowReference(i);
                ref double rC1 = ref C.GetRowReference(i + 1);
                // Get references to the start of accumulator arrays
                ref var refC0 = ref MemoryMarshal.GetReference(vC0);
                ref var refC1 = ref MemoryMarshal.GetReference(vC1);
                // Clear/initialize for this i-iteration
                for (int j = 0; j < VectorsPerRow; ++j)
                {
                    Unsafe.Add(ref refC0, j) = Vector512<double>.Zero;
                    Unsafe.Add(ref refC1, j) = Vector512<double>.Zero;
                }
                for (int k = 0; k < KernelSize; ++k)
                {
                    var vA0 = Vector512.Create(A[i, k]);
                    var vA1 = Vector512.Create(A[i + 1, k]);
                    ref double rB = ref B.GetRowReference(k);

                    // Process all 8 vectors
                    for (int j = 0; j < VectorsPerRow; ++j)
                    {
                        var vB = Unsafe.As<double, Vector512<double>>(ref rB);
                        Unsafe.Add(ref refC0, j) = Avx512F.FusedMultiplyAdd(vA0, vB, Unsafe.Add(ref refC0, j));
                        Unsafe.Add(ref refC1, j) = Avx512F.FusedMultiplyAdd(vA1, vB, Unsafe.Add(ref refC1, j));
                        rB = ref Unsafe.Add(ref rB, VectorSize);
                    }
                }

                // Store results
                ref double pC0 = ref rC0;
                ref double pC1 = ref rC1;
                for (int j = 0; j < VectorsPerRow; ++j)
                {
                    Unsafe.As<double, Vector512<double>>(ref pC0) = Unsafe.Add(ref refC0, j);
                    Unsafe.As<double, Vector512<double>>(ref pC1) = Unsafe.Add(ref refC1, j);
                    pC0 = ref Unsafe.Add(ref pC0, VectorSize);
                    pC1 = ref Unsafe.Add(ref pC1, VectorSize);
                }
            }
        }

        // FMA version for AVX2
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void MultiplyFmaKernel_64x64(MatrixView A, MatrixView B, MatrixView C)
        {
            const int VectorSize = 4;
            const int VectorsPerRow = KernelSize / VectorSize;

            // Allocate outside the loop
            Span<Vector256<double>> vC0 = stackalloc Vector256<double>[VectorsPerRow];
            Span<Vector256<double>> vC1 = stackalloc Vector256<double>[VectorsPerRow];

            for (int i = 0; i < KernelSize; i += 2)
            {
                ref double rC0 = ref C.GetRowReference(i);
                ref double rC1 = ref C.GetRowReference(i + 1);
                // Get references to the start of accumulator arrays
                ref var refC0 = ref MemoryMarshal.GetReference(vC0);
                ref var refC1 = ref MemoryMarshal.GetReference(vC1);

                // Clear/initialize for this i-iteration
                for (int j = 0; j < VectorsPerRow; ++j)
                {
                    Unsafe.Add(ref refC0, j) = Vector256<double>.Zero;
                    Unsafe.Add(ref refC1, j) = Vector256<double>.Zero;
                }
                for (int k = 0; k < KernelSize; ++k)
                {
                    var vA0 = Vector256.Create(A[i, k]);
                    var vA1 = Vector256.Create(A[i + 1, k]);
                    ref double rB = ref B.GetRowReference(k);

                    // Process all 8 vectors
                    for (int j = 0; j < VectorsPerRow; ++j)
                    {
                        var vB = Unsafe.As<double, Vector256<double>>(ref rB);
                        Unsafe.Add(ref refC0, j) = Fma.MultiplyAdd(vA0, vB, Unsafe.Add(ref refC0, j));
                        Unsafe.Add(ref refC1, j) = Fma.MultiplyAdd(vA1, vB, Unsafe.Add(ref refC1, j));
                        rB = ref Unsafe.Add(ref rB, VectorSize);
                    }
                }

                // Store results
                ref double pC0 = ref rC0;
                ref double pC1 = ref rC1;
                for (int j = 0; j < VectorsPerRow; ++j)
                {
                    Unsafe.As<double, Vector256<double>>(ref pC0) = Unsafe.Add(ref refC0, j);
                    Unsafe.As<double, Vector256<double>>(ref pC1) = Unsafe.Add(ref refC1, j);
                    pC0 = ref Unsafe.Add(ref pC0, VectorSize);
                    pC1 = ref Unsafe.Add(ref pC1, VectorSize);
                }
            }
        }
    }
}
