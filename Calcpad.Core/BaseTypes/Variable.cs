using System;
using System.Threading;

namespace Calcpad.Core
{
    internal class Variable
    {
        internal IValue Value => _value;
        private IValue _value;
        internal ref IValue ValueByRef() => ref _value;
        internal event Action OnChange;
        internal void Change() => OnChange?.Invoke();
        internal bool IsInitialized => _isIntialised;
        internal bool IsReadOnly;
        private bool _isIntialised;
        // Counts first assignments, so cached equations know when to recheck
        internal static int Initializations => Volatile.Read(ref _initializations);
        private static int _initializations;

        internal Variable(in IValue value)
        {
            _value = value;
            Initialize();
        }

        private void Initialize()
        {
            if (_isIntialised)
                return;

            _isIntialised = true;
            Interlocked.Increment(ref _initializations);
        }

        internal Variable(in Complex number) : this(new ComplexValue(number)) { }
        public Variable() { }
        internal void SetNumber(in Complex number)
        {
            ref var iVal = ref ValueByRef();
            if (iVal is RealValue real)
                _value = new ComplexValue(number, real.Units);
            else if (iVal is ComplexValue complex)
                _value = new ComplexValue(number, complex.Units);
            else
                _value = new ComplexValue(number);

        }
        internal void SetNumber(double number)
        {
            ref var iVal = ref ValueByRef();
            if (iVal is RealValue real)
                _value = new RealValue(number, real.Units);
            else if (iVal is ComplexValue complex)
                _value = new RealValue(number, complex.Units);
            else
                _value = new ComplexValue(number);
        }

        internal void SetUnits(Unit units)
        {
            ref var value = ref ValueByRef();
            if (value is RealValue real)
                _value = new RealValue(real.D, units);
            else if (value is ComplexValue complex)
                _value = new ComplexValue(complex.A, complex.B, units);
            else
                ((Vector)Value).SetUnits(units);

        }
        internal void SetValue(Unit units) => _value = new RealValue(units);
        internal void SetValue(double number, Unit units)
        {
            _value = new RealValue(number, units);
            Initialize();
        }

        internal void SetValue(in IValue value)
        {
            _value = value;
            Initialize();
        }

        internal void Assign(in IValue value)
        {
            _value = value;
            Initialize();
            OnChange?.Invoke();
            if (value is Vector vector)
                vector.OnChange += Change;
            else if (value is Matrix matrix)
                matrix.OnChange += Change;
        }
    }
}
