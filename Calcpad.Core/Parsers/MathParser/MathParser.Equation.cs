using System;
using System.Collections.Generic;

namespace Calcpad.Core
{
    public partial class MathParser
    {
        private sealed class Equation
        {
            internal Token[] Rpn;
            internal Unit TargetUnits;
            internal Func<IValue> Function;
            private readonly Dictionary<string, Variable> _variables;
            // Unassigned when cached; block-local ones are checked against the global of that name
            private readonly VariableToken[] _globals, _locals;
            private int _checkedAt = Variable.Initializations - 1;
            internal Equation(Token[] rpn, Unit targetUnits, Func<IValue> function, MathParser parser)
            {
                Rpn = rpn;
                TargetUnits = targetUnits;
                Function = function;
                _variables = parser._variables;
                List<VariableToken> globals = null, locals = null;
                Collect(rpn);
                _globals = globals is null ? [] : [.. globals];
                _locals = locals is null ? [] : [.. locals];

                void Collect(Token[] tokens)
                {
                    // An assignment target is never read as a unit
                    var i0 = tokens.Length > 1 && IsAssignment(tokens[^1].Content) ? 1 : 0;
                    for (var i = i0; i < tokens.Length; ++i)
                        if (tokens[i] is VariableToken { Variable.IsInitialized: false } vt)
                        {
                            if (_variables.TryGetValue(vt.Content, out var v) && ReferenceEquals(v, vt.Variable))
                                (globals ??= []).Add(vt);
                            else
                                (locals ??= []).Add(vt);
                        }
                        else if (tokens[i].Type == TokenTypes.Solver)
                            foreach (var itemRpn in parser._solveBlocks[(int)tokens[i].Index].ItemRpns())
                                Collect(itemRpn);
                }
            }

            // Parse again once a global is assigned that a unit fallback or a block placeholder missed
            internal bool IsStale => _checkedAt != Variable.Initializations && CheckStale();

            private bool CheckStale()
            {
                _checkedAt = Variable.Initializations;
                foreach (var t in _globals)
                    if (t.Type == TokenTypes.Unit && t.Variable.IsInitialized)
                        return true;

                foreach (var t in _locals)
                    if ((t.Type == TokenTypes.Unit || !t.Variable.IsInitialized) &&
                        _variables.TryGetValue(t.Content, out var v) && v.IsInitialized)
                        return true;

                return false;
            }
        }
    }
}
