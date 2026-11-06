namespace Calcpad.Core
{
    // °C/°F as absolute temperatures. Only wired in when the setting is on, so the default path stays untouched.
    internal static class AbsoluteTemperatureRules
    {
        private enum Action
        {
            None,
            Base,
            Delta,
            BaseB,
            Swap
        }

        private delegate T Convert<T>(in T value);

        internal static bool IsAffected(char op) =>
            op is '+' or '-' or '^' or '/' or '÷' or '\\' or '*' or '<' or '>' or '≤' or '≥' or '≡' or '≠';

        // T + ΔT stays absolute, T − T gives ΔT, everything else converts T to K/°R first
        private static Action GetAction(char op, bool aAbs, bool bAbs) => op switch
        {
            '+' => bAbs ? aAbs ? Action.Base : Action.Swap : Action.None,
            '-' => bAbs ? aAbs ? Action.Delta : Action.BaseB : Action.None,
            _ => aAbs || bAbs ? Action.Base : Action.None
        };

        private static bool IsAbs(Unit u, bool isUnit) => !isUnit && u is not null && u.IsAbsoluteTemp;

        private static bool IsAbs<T>(in T v) where T : struct, IScalarValue => IsAbs(v.Units, v.IsUnit);

        internal static RealValue ToBase(in RealValue v) =>
            IsAbs(v.Units, v.IsUnit) ? new(v.D + v.Units.AbsoluteTempOffset, v.Units.AbsoluteTempBase) : v;

        private static RealValue ToDelta(in RealValue v) =>
            IsAbs(v.Units, v.IsUnit) ? new(v.D + v.Units.AbsoluteTempOffset, v.Units.TempDelta) : v;

        private static ComplexValue ToBase(in ComplexValue v) =>
            IsAbs(v.Units, v.IsUnit) ? new(v.A + v.Units.AbsoluteTempOffset, v.B, v.Units.AbsoluteTempBase) : v;

        private static ComplexValue ToDelta(in ComplexValue v) =>
            IsAbs(v.Units, v.IsUnit) ? new(v.A + v.Units.AbsoluteTempOffset, v.B, v.Units.TempDelta) : v;

        #region Scalar tables
        internal static Calculator.Operator<RealValue>[] WrapOperators(Calculator.Operator<RealValue>[] ops) =>
            WrapOperators(ops, ToBase, ToDelta);

        internal static Calculator.Operator<ComplexValue>[] WrapOperators(Calculator.Operator<ComplexValue>[] ops) =>
            WrapOperators(ops, ToBase, ToDelta);

        // Converts both operands to K/°R, as for powers and roots
        internal static Calculator.Operator<RealValue> WrapBase(Calculator.Operator<RealValue> op) =>
            Wrap(op, '^', ToBase, ToDelta);

        internal static Calculator.Function<RealValue> WrapRoot(Calculator.Function<RealValue> f) =>
            (in RealValue a) => f(ToBase(a));

        internal static Calculator.Function<RealValue>[] WrapRoots(Calculator.Function<RealValue>[] fns) =>
            WrapRoots(fns, ToBase);

        internal static Calculator.Function<ComplexValue>[] WrapRoots(Calculator.Function<ComplexValue>[] fns) =>
            WrapRoots(fns, ToBase);

        internal static Calculator.Operator<RealValue>[] WrapRoots2(Calculator.Operator<RealValue>[] fns) =>
            WrapRoots2(fns, ToBase, ToDelta);

        internal static Calculator.Operator<ComplexValue>[] WrapRoots2(Calculator.Operator<ComplexValue>[] fns) =>
            WrapRoots2(fns, ToBase, ToDelta);

        private static Calculator.Operator<T>[] WrapOperators<T>(Calculator.Operator<T>[] ops, Convert<T> toBase, Convert<T> toDelta)
            where T : struct, IScalarValue
        {
            var result = new Calculator.Operator<T>[ops.Length];
            for (int i = 0; i < ops.Length; ++i)
            {
                var symbol = Calculator.GetOperatorSymbol(i);
                result[i] = IsAffected(symbol) ? Wrap(ops[i], symbol, toBase, toDelta) : ops[i];
            }
            return result;
        }

        private static Calculator.Operator<T> Wrap<T>(Calculator.Operator<T> op, char symbol, Convert<T> toBase, Convert<T> toDelta)
            where T : struct, IScalarValue =>
            (in T a, in T b) => GetAction(symbol, IsAbs(a), IsAbs(b)) switch
            {
                Action.Base => op(toBase(a), toBase(b)),
                Action.Delta => op(toDelta(a), toDelta(b)),
                Action.BaseB => op(a, toBase(b)),
                Action.Swap => op(b, a),
                _ => op(a, b)
            };

        private static Calculator.Function<T>[] WrapRoots<T>(Calculator.Function<T>[] fns, Convert<T> toBase)
            where T : struct, IScalarValue
        {
            var result = (Calculator.Function<T>[])fns.Clone();
            int[] indices = [Calculator.SqrFunctionIndex, Calculator.SqrtFunctionIndex, Calculator.CbrtFunctionIndex];
            foreach (var i in indices)
            {
                var f = fns[i];
                result[i] = (in T a) => f(toBase(a));
            }
            return result;
        }

        private static Calculator.Operator<T>[] WrapRoots2<T>(Calculator.Operator<T>[] fns, Convert<T> toBase, Convert<T> toDelta)
            where T : struct, IScalarValue
        {
            var result = (Calculator.Operator<T>[])fns.Clone();
            var i = Calculator.RootFunctionIndex;
            result[i] = Wrap(fns[i], '^', toBase, toDelta);
            return result;
        }
        #endregion

        #region Vectors and matrices
        internal static IValue EvaluateOperator(MatrixCalculator calc, long index, IValue a, IValue b)
        {
            var symbol = Calculator.GetOperatorSymbol(index);
            if (!IsAffected(symbol))
                return IValue.EvaluateOperatorCore(calc, index, a, b);

            switch (GetAction(symbol, IsAbs(a), IsAbs(b)))
            {
                case Action.Base:
                    return IValue.EvaluateOperatorCore(calc, index, ToBase(a), ToBase(b));
                case Action.Delta:
                    return IValue.EvaluateOperatorCore(calc, index, ToDelta(a), ToDelta(b));
                case Action.BaseB:
                    return IValue.EvaluateOperatorCore(calc, index, a, ToBase(b));
                case Action.Swap:
                    if (b is RealValue rb)
                        return Map(a, (in RealValue x) => rb + x);

                    return IValue.EvaluateOperatorCore(calc, index, b, a);
                default:
                    return IValue.EvaluateOperatorCore(calc, index, a, b);
            }
        }

        internal static IValue ToBase(IValue v) => v switch
        {
            RealValue r => ToBase(r),
            ComplexValue c => ToBase(c),
            _ => IsAbs(v) ? Map(v, ToBase) : v
        };

        private static IValue ToDelta(IValue v) => v switch
        {
            RealValue r => ToDelta(r),
            ComplexValue c => ToDelta(c),
            _ => IsAbs(v) ? Map(v, ToDelta) : v
        };

        private static bool IsAbs(IValue v)
        {
            switch (v)
            {
                case RealValue r:
                    return IsAbs(r);
                case ComplexValue c:
                    return IsAbs(c);
                case HpVector hp:
                    return IsAbs(hp.Units, false);
                case HpMatrix hm:
                    return IsAbs(hm.Units, false);
                case Vector vec:
                    for (int i = vec.Size - 1; i >= 0; --i)
                        if (IsAbs(vec[i]))
                            return true;

                    return false;
                case Matrix m:
                    for (int i = m.RowCount - 1; i >= 0; --i)
                        for (int j = m.ColCount - 1; j >= 0; --j)
                            if (IsAbs(m[i, j]))
                                return true;

                    return false;
                default:
                    return false;
            }
        }

        private static IValue Map(IValue v, Calculator.Function<RealValue> f) => v switch
        {
            RealValue r => f(r),
            HpVector hp => HpVector.EvaluateFunction(f, hp),
            Vector vec => Vector.EvaluateFunction(f, vec),
            HpMatrix hm => HpMatrix.EvaluateFunction(f, hm, -2),
            Matrix m => Matrix.EvaluateFunction(f, m, -2),
            _ => v
        };
        #endregion
    }
}
