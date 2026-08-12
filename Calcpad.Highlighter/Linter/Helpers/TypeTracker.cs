using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Snippets;
using Calcpad.Highlighter.Tokenizer.Models;

namespace Calcpad.Highlighter.Linter.Helpers
{
    /// <summary>
    /// Tracks and infers types for Calcpad variables, functions, and macros.
    /// </summary>
    public class TypeTracker
    {
        private readonly Dictionary<string, VariableInfo> _variables = new(StringComparer.Ordinal);
        private readonly Dictionary<string, VariableInfo> _functions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, VariableInfo> _macros = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VariableInfo> _customUnits = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<int, List<Token>> _tokensByLine;

        /// <summary>
        /// Provides per-line tokens from the tokenizer so that type inference can use
        /// actual token boundaries instead of re-scanning raw expression strings.
        /// </summary>
        public void SetTokensByLine(Dictionary<int, List<Token>> tokensByLine)
        {
            _tokensByLine = tokensByLine;
        }

        // Patterns for type inference
        private static readonly Regex VectorLiteralPattern = new(
            @"^\s*\[[^\|]+\]\s*$",
            RegexOptions.Compiled);

        private static readonly Regex MatrixLiteralPattern = new(
            @"^\s*\[.+\|.+\]\s*$",
            RegexOptions.Compiled);

        // Functions that return vectors - derived from SnippetRegistry
        private static FrozenSet<string> VectorReturningFunctions => SnippetRegistry.GetVectorReturningFunctions();

        // Functions that return matrices - derived from SnippetRegistry
        private static FrozenSet<string> MatrixReturningFunctions => SnippetRegistry.GetMatrixReturningFunctions();

        /// <summary>
        /// Gets all tracked variables
        /// </summary>
        public IReadOnlyDictionary<string, VariableInfo> Variables => _variables;

        /// <summary>
        /// Gets all tracked functions
        /// </summary>
        public IReadOnlyDictionary<string, VariableInfo> Functions => _functions;

        /// <summary>
        /// Gets all tracked macros
        /// </summary>
        public IReadOnlyDictionary<string, VariableInfo> Macros => _macros;

        /// <summary>
        /// Gets all tracked custom units
        /// </summary>
        public IReadOnlyDictionary<string, VariableInfo> CustomUnits => _customUnits;

        /// <summary>
        /// Registers a variable assignment and infers its type from the expression.
        /// If the variable was already defined with a different type, marks it as Various.
        /// </summary>
        public VariableInfo RegisterVariable(string name, string expression, int lineNumber, int column = 0, string source = "local", bool isConst = false)
        {
            var newType = InferTypeFromExpression(expression, lineNumber);

            // Check if variable already exists with a different type
            if (_variables.TryGetValue(name, out var existing))
            {
                if (existing.Type != newType && existing.Type != CalcpadType.Various)
                {
                    // Type changed - mark as Various
                    existing.Type = CalcpadType.Various;
                    return existing;
                }
                // Same type or already Various - keep existing
                return existing;
            }

            var info = new VariableInfo
            {
                Name = name,
                Expression = expression,
                LineNumber = lineNumber,
                Column = column,
                Source = source,
                Type = newType,
                IsConst = isConst
            };

            _variables[name] = info;
            return info;
        }

        /// <summary>
        /// Registers a function definition and infers its return type from the expression.
        /// </summary>
        public VariableInfo RegisterFunction(string name, List<string> parameters, string expression, int lineNumber, int column = 0, string source = "local", bool isConst = false)
        {
            var returnType = InferTypeFromExpression(expression);

            var info = new VariableInfo
            {
                Name = name,
                Parameters = parameters,
                Expression = expression,
                LineNumber = lineNumber,
                Column = column,
                Source = source,
                Type = CalcpadType.Function,
                ReturnType = returnType,
                IsConst = isConst
            };

            _functions[name] = info;
            return info;
        }

        /// <summary>
        /// Registers a function definition with a command block.
        /// The return type is inferred from the last statement in the block.
        /// </summary>
        public VariableInfo RegisterCommandBlockFunction(string name, List<string> parameters, List<string> statements, int lineNumber, int column = 0, string source = "local", bool isConst = false)
        {
            // Infer return type from the last statement in the block
            var returnType = CalcpadType.Unknown;
            if (statements != null && statements.Count > 0)
            {
                var lastStatement = statements[statements.Count - 1];
                returnType = InferReturnTypeFromStatement(lastStatement);
            }

            var info = new VariableInfo
            {
                Name = name,
                Parameters = parameters,
                Expression = string.Join("; ", statements ?? new List<string>()),
                LineNumber = lineNumber,
                Column = column,
                Source = source,
                Type = CalcpadType.Function,
                ReturnType = returnType,
                IsConst = isConst
            };

            _functions[name] = info;
            return info;
        }

        /// <summary>
        /// Infers the return type from a statement (used for command blocks).
        /// Handles assignment statements by extracting the right-hand side.
        /// </summary>
        private CalcpadType InferReturnTypeFromStatement(string statement)
        {
            if (string.IsNullOrWhiteSpace(statement))
                return CalcpadType.Unknown;

            var trimmed = statement.Trim();

            // If it's an assignment, extract the right-hand side
            // Pattern: identifier = expression (but not ==, <=, >=, !=)
            var assignmentIndex = -1;
            for (int i = 0; i < trimmed.Length - 1; i++)
            {
                if (trimmed[i] == '=' && trimmed[i + 1] != '=')
                {
                    // Make sure it's not part of <=, >=, != (check char before)
                    if (i > 0 && (trimmed[i - 1] == '<' || trimmed[i - 1] == '>' || trimmed[i - 1] == '!'))
                        continue;
                    assignmentIndex = i;
                    break;
                }
            }

            if (assignmentIndex > 0)
            {
                var rightSide = trimmed.Substring(assignmentIndex + 1).Trim();
                return InferTypeFromExpression(rightSide);
            }

            // Not an assignment - try to infer from the expression directly
            return InferTypeFromExpression(trimmed);
        }

        /// <summary>
        /// Registers an inline macro definition.
        /// </summary>
        public VariableInfo RegisterInlineMacro(string name, List<string> parameters, string expression, int lineNumber, int column = 0, string source = "local")
        {
            var info = new VariableInfo
            {
                Name = name,
                Parameters = parameters,
                Expression = expression,
                LineNumber = lineNumber,
                Column = column,
                Source = source,
                Type = CalcpadType.InlineMacro
            };

            _macros[name] = info;
            return info;
        }

        /// <summary>
        /// Registers a multiline macro definition.
        /// </summary>
        public VariableInfo RegisterMultilineMacro(string name, List<string> parameters, int lineNumber, int column = 0, string source = "local")
        {
            var info = new VariableInfo
            {
                Name = name,
                Parameters = parameters,
                LineNumber = lineNumber,
                Column = column,
                Source = source,
                Type = CalcpadType.MultilineMacro
            };

            _macros[name] = info;
            return info;
        }

        /// <summary>
        /// Registers a variable from a #read statement.
        /// Type is Matrix for TYPE=R (row), Vector for TYPE=V.
        /// </summary>
        public VariableInfo RegisterReadVariable(string name, bool isVector, int lineNumber, int column = 0, string source = "local")
        {
            var info = new VariableInfo
            {
                Name = name,
                Expression = "#read",
                LineNumber = lineNumber,
                Column = column,
                Source = source,
                Type = isVector ? CalcpadType.Vector : CalcpadType.Matrix
            };

            _variables[name] = info;
            return info;
        }

        /// <summary>
        /// Registers a custom unit definition.
        /// </summary>
        public VariableInfo RegisterCustomUnit(string unitName, string expression, int lineNumber, int column = 0, string source = "local")
        {
            var info = new VariableInfo
            {
                Name = "." + unitName,
                UnitName = unitName,
                Expression = expression,
                LineNumber = lineNumber,
                Column = column,
                Source = source,
                Type = CalcpadType.CustomUnit
            };

            _customUnits[unitName] = info;
            return info;
        }

        /// <summary>
        /// Gets the type of a variable by name.
        /// </summary>
        public CalcpadType GetVariableType(string name)
        {
            // Strip trailing dot for element access syntax
            var lookupName = name.EndsWith(".") ? name.Substring(0, name.Length - 1) : name;

            if (_variables.TryGetValue(lookupName, out var varInfo))
                return varInfo.Type;

            if (_functions.ContainsKey(lookupName))
                return CalcpadType.Function;

            if (_macros.ContainsKey(lookupName))
                return _macros[lookupName].Type;

            if (_customUnits.ContainsKey(lookupName))
                return CalcpadType.CustomUnit;

            return CalcpadType.Unknown;
        }

        /// <summary>
        /// Gets the return type of a function by name.
        /// Returns Unknown if the function is not found.
        /// </summary>
        public CalcpadType GetFunctionReturnType(string name)
        {
            if (_functions.TryGetValue(name, out var funcInfo))
                return funcInfo.ReturnType;

            return CalcpadType.Unknown;
        }

        /// <summary>
        /// Gets variable info by name. Returns null if not found.
        /// </summary>
        public VariableInfo GetVariableInfo(string name)
        {
            var lookupName = name.EndsWith(".") ? name.Substring(0, name.Length - 1) : name;

            if (_variables.TryGetValue(lookupName, out var varInfo))
                return varInfo;

            if (_functions.TryGetValue(lookupName, out var funcInfo))
                return funcInfo;

            if (_macros.TryGetValue(lookupName, out var macroInfo))
                return macroInfo;

            if (_customUnits.TryGetValue(lookupName, out var unitInfo))
                return unitInfo;

            return null;
        }

        /// <summary>
        /// Checks if a variable name is defined.
        /// </summary>
        public bool IsDefined(string name)
        {
            var lookupName = name.EndsWith(".") ? name.Substring(0, name.Length - 1) : name;

            return _variables.ContainsKey(lookupName) ||
                   _functions.ContainsKey(lookupName) ||
                   _macros.ContainsKey(lookupName) ||
                   _customUnits.ContainsKey(lookupName);
        }

        /// <summary>
        /// Checks if element access (.() syntax) is valid for this variable.
        /// </summary>
        public bool SupportsElementAccess(string name)
        {
            var lookupName = name.EndsWith(".") ? name.Substring(0, name.Length - 1) : name;

            if (_variables.TryGetValue(lookupName, out var info))
                return info.SupportsElementAccess;

            return false;
        }

        /// <summary>
        /// Infers the type of a value from its expression.
        /// </summary>
        public CalcpadType InferTypeFromExpression(string expression, int lineNumber = -1)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return CalcpadType.Unknown;

            var trimmed = expression.Trim();

            // Check for matrix literal first (contains |)
            if (MatrixLiteralPattern.IsMatch(trimmed))
                return CalcpadType.Matrix;

            // Check for vector literal (contains ; but no |)
            if (VectorLiteralPattern.IsMatch(trimmed))
                return CalcpadType.Vector;

            // Check for element access on a vector/matrix (returns scalar)
            // Patterns: M.(1;1), v.i, v.3, v.(expr)
            var elementAccessType = InferTypeFromElementAccess(trimmed);
            if (elementAccessType != CalcpadType.Unknown)
                return elementAccessType;

            // Check for function calls that return vectors or matrices
            var funcCallType = InferTypeFromFunctionCall(trimmed);
            if (funcCallType != CalcpadType.Unknown)
                return funcCallType;

            // Check if referencing a known variable
            var refType = InferTypeFromVariableReference(trimmed);
            if (refType != CalcpadType.Unknown)
                return refType;

            // Check complex expressions with operators for vector/matrix operands
            // e.g., "t_ov - t_cmu" where both are vectors → result is a vector
            if (trimmed.IndexOfAny(['+', '-', '*', '/', '^', '(']) >= 0)
            {
                // Use tokens when available — they handle commas, dots, and Greek
                // letters in variable names correctly, avoiding broken identifier scanning.
                var expressionTokens = GetExpressionTokens(lineNumber);
                var operandType = expressionTokens != null
                    ? InferTypeFromOperandTokens(expressionTokens)
                    : InferTypeFromOperandTypes(trimmed);
                if (operandType != CalcpadType.Unknown)
                    return operandType;
            }

            // Default to unknown - don't assume scalar if we can't determine the type
            return CalcpadType.Unknown;
        }

        private CalcpadType InferTypeFromFunctionCall(string expression)
        {
            // Look for function call pattern: functionName(...)
            var parenIndex = expression.IndexOf('(');
            if (parenIndex <= 0)
                return CalcpadType.Unknown;

            // Extract function name (handling possible operators before it)
            var beforeParen = expression.Substring(0, parenIndex);
            var funcName = ExtractLastIdentifier(beforeParen);

            if (string.IsNullOrEmpty(funcName))
                return CalcpadType.Unknown;

            // Check built-in vector-returning functions
            if (VectorReturningFunctions.Contains(funcName))
                return CalcpadType.Vector;

            // Check built-in matrix-returning functions
            if (MatrixReturningFunctions.Contains(funcName))
                return CalcpadType.Matrix;

            // Check user-defined functions
            if (_functions.TryGetValue(funcName, out var funcInfo))
            {
                // Return the function's return type if known
                if (funcInfo.ReturnType != CalcpadType.Unknown)
                    return funcInfo.ReturnType;
            }

            // Check built-in function signatures (covers string functions and others with ReturnType)
            var signature = FunctionSignatures.GetSignature(funcName);
            if (signature != null && signature.ReturnType != CalcpadType.Value)
                return signature.ReturnType;

            return CalcpadType.Unknown;
        }

        private CalcpadType InferTypeFromVariableReference(string expression)
        {
            // Simple case: expression is just a variable name
            var trimmed = expression.Trim();

            // Skip if it contains operators (complex expression)
            if (trimmed.IndexOfAny(new[] { '+', '-', '*', '/', '^', '(', ')' }) >= 0)
                return CalcpadType.Unknown;

            if (_variables.TryGetValue(trimmed, out var info))
                return info.Type;

            return CalcpadType.Unknown;
        }

        /// <summary>
        /// Detects element access patterns on vectors/matrices, which always return a scalar.
        /// Patterns: v.1, v.i, v.(expr), M.(1;2)
        /// </summary>
        private CalcpadType InferTypeFromElementAccess(string expression)
        {
            // Find the first dot that could be element access
            var dotIndex = expression.IndexOf('.');
            if (dotIndex <= 0 || dotIndex >= expression.Length - 1)
                return CalcpadType.Unknown;

            var varName = expression.Substring(0, dotIndex);

            // The base name must be a known vector, matrix, or Various-type variable
            if (!_variables.TryGetValue(varName, out var info))
                return CalcpadType.Unknown;

            if (info.Type != CalcpadType.Vector && info.Type != CalcpadType.Matrix && info.Type != CalcpadType.Various)
                return CalcpadType.Unknown;

            // After the dot must be a digit, identifier char, or opening paren
            var afterDot = expression[dotIndex + 1];
            if (char.IsLetterOrDigit(afterDot) || afterDot == '(')
                return CalcpadType.Value;

            return CalcpadType.Unknown;
        }

        /// <summary>
        /// Infers type from a complex expression containing operators.
        /// Scans for top-level variable references and function calls within the expression.
        /// If any operand is a known vector or matrix, the result type propagates
        /// (vector op vector = vector, matrix op anything = matrix, etc.).
        /// Function call arguments are skipped (balanced parens) so that e.g. len(vec)
        /// uses len's return type (scalar), not vec's type.
        /// </summary>
        private CalcpadType InferTypeFromOperandTypes(string expression)
        {
            var atoms = new List<CalcpadType>();
            var ops = new List<char>();
            char pendingOp = '\0';
            int i = 0;
            int len = expression.Length;

            while (i < len)
            {
                char c = expression[i];

                if (CalcpadCharacterHelpers.IsIdentifierStartCharWithUnderscore(c))
                {
                    int start = i++;
                    while (i < len && CalcpadCharacterHelpers.IsIdentifierChar(expression[i]))
                        i++;

                    var name = expression.Substring(start, i - start);

                    CalcpadType atom;
                    if (i < len && expression[i] == '(')
                    {
                        atom = GetFunctionCallReturnType(name);
                        // Skip balanced parentheses so arguments aren't examined as top-level operands
                        i = SkipBalancedParentheses(expression, i);
                    }
                    else
                    {
                        atom = _variables.TryGetValue(name, out var info) ? info.Type : CalcpadType.Unknown;
                    }
                    atoms.Add(atom);
                    ops.Add(pendingOp);
                    pendingOp = '\0';
                }
                else if (char.IsDigit(c))
                {
                    i++;
                    while (i < len && (char.IsDigit(expression[i]) || expression[i] == '.'))
                        i++;
                    atoms.Add(CalcpadType.Value);
                    ops.Add(pendingOp);
                    pendingOp = '\0';
                }
                else
                {
                    if ((c == '+' || c == '-' || c == '*' || c == '/' || c == '^') && pendingOp == '\0')
                        pendingOp = c;
                    i++;
                }
            }

            return FoldOperandTypes(atoms, ops);
        }

        /// <summary>
        /// Returns the expression-portion tokens for a definition line (tokens after the '='),
        /// or null if tokens are not available for the line.
        /// </summary>
        private List<Token> GetExpressionTokens(int lineNumber)
        {
            if (lineNumber < 0 || _tokensByLine == null ||
                !_tokensByLine.TryGetValue(lineNumber, out var lineTokens))
                return null;

            // Find the '=' operator and return everything after it
            for (int i = 0; i < lineTokens.Count; i++)
            {
                if (lineTokens[i].Type == TokenType.Operator && lineTokens[i].Text == "=")
                    return lineTokens.GetRange(i + 1, lineTokens.Count - i - 1);
            }
            return null;
        }

        /// <summary>
        /// Token-based version of InferTypeFromOperandTypes. Uses actual tokenizer tokens
        /// to correctly handle commas in variable names, element access dots, etc.
        /// </summary>
        private CalcpadType InferTypeFromOperandTokens(List<Token> tokens)
        {
            var atoms = new List<CalcpadType>();
            var ops = new List<char>();
            char pendingOp = '\0';
            int depth = 0;

            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];

                if (token.Type == TokenType.Bracket)
                {
                    if (token.Text == "(" || token.Text == "[" || token.Text == "{") depth++;
                    else if (token.Text == ")" || token.Text == "]" || token.Text == "}") depth--;
                    continue;
                }

                if (depth > 0)
                    continue;

                // Record the binary operator between operands ('.' is element access, handled below).
                if (token.Type == TokenType.Operator)
                {
                    if (token.Text != "." && pendingOp == '\0')
                        pendingOp = token.Text[0];
                    continue;
                }

                CalcpadType atom;
                if (token.Type == TokenType.Function)
                {
                    atom = GetFunctionCallReturnType(token.Text);
                }
                else if (token.Type == TokenType.Variable || token.Type == TokenType.LocalVariable)
                {
                    // Element access (v.1, v.i) yields a scalar — consume the index token.
                    // Bracket-form indices like v.(expr) and M.(1;2) are handled by depth tracking.
                    if (i + 1 < tokens.Count &&
                        tokens[i + 1].Type == TokenType.Operator &&
                        tokens[i + 1].Text == ".")
                    {
                        i++;
                        if (i + 1 < tokens.Count)
                        {
                            var idx = tokens[i + 1];
                            if (idx.Type == TokenType.Variable ||
                                idx.Type == TokenType.LocalVariable ||
                                idx.Type == TokenType.Const)
                            {
                                i++;
                            }
                        }
                        atom = CalcpadType.Value;
                    }
                    else
                    {
                        atom = _variables.TryGetValue(token.Text, out var info) ? info.Type : CalcpadType.Unknown;
                    }
                }
                else if (token.Type == TokenType.Const)
                {
                    atom = CalcpadType.Value;
                }
                else
                {
                    continue;
                }

                atoms.Add(atom);
                ops.Add(pendingOp);
                pendingOp = '\0';
            }

            return FoldOperandTypes(atoms, ops);
        }

        /// <summary>
        /// Folds a sequence of operand types into the resulting type. Multiplicative chains are
        /// collapsed first so that Core's matrix·vector = vector rule is respected; remaining
        /// (additive) terms follow "matrix dominates, then vector".
        /// </summary>
        private static CalcpadType FoldOperandTypes(List<CalcpadType> atoms, List<char> ops)
        {
            if (atoms.Count == 0)
                return CalcpadType.Unknown;

            var terms = new List<CalcpadType> { atoms[0] };
            for (int i = 1; i < atoms.Count; i++)
            {
                if (ops[i] == '*')
                    terms[^1] = CombineMultiply(terms[^1], atoms[i]);
                else
                    terms.Add(atoms[i]);
            }

            var result = CalcpadType.Unknown;
            foreach (var t in terms)
            {
                if (t == CalcpadType.Matrix) return CalcpadType.Matrix;
                if (t == CalcpadType.Vector) result = CalcpadType.Vector;
            }
            return result;
        }

        /// <summary>
        /// Type of a multiplicative product. Core unwraps a matrix·vector product to a vector
        /// (IValue.EvaluateOperator); every other combination keeps the dominant type.
        /// </summary>
        private static CalcpadType CombineMultiply(CalcpadType left, CalcpadType right)
        {
            if (left == CalcpadType.Matrix && right == CalcpadType.Vector)
                return CalcpadType.Vector;
            if (left == CalcpadType.Matrix || right == CalcpadType.Matrix)
                return CalcpadType.Matrix;
            if (left == CalcpadType.Vector || right == CalcpadType.Vector)
                return CalcpadType.Vector;
            return left != CalcpadType.Unknown ? left : right;
        }

        /// <summary>
        /// Gets the return type of a function call by checking built-in signatures
        /// and user-defined functions. Returns Unknown if not found.
        /// </summary>
        private CalcpadType GetFunctionCallReturnType(string funcName)
        {
            // Check built-in function signatures (includes return type from snippets)
            var signature = FunctionSignatures.GetSignature(funcName);
            if (signature != null)
                return signature.ReturnType;

            // Check user-defined functions
            if (_functions.TryGetValue(funcName, out var funcInfo))
                return funcInfo.ReturnType;

            return CalcpadType.Unknown;
        }

        /// <summary>
        /// Advances past a balanced pair of parentheses starting at expression[startIndex] = '('.
        /// Returns the index after the closing ')'.
        /// </summary>
        private static int SkipBalancedParentheses(string expression, int startIndex)
        {
            int depth = 0;
            int i = startIndex;
            int len = expression.Length;

            while (i < len)
            {
                if (expression[i] == '(') depth++;
                else if (expression[i] == ')') { depth--; if (depth == 0) return i + 1; }
                i++;
            }

            return i; // Unbalanced - return end of string
        }

        private static string ExtractLastIdentifier(string text)
        {
            // Work backwards to find the last identifier
            var end = text.Length - 1;
            while (end >= 0 && char.IsWhiteSpace(text[end]))
                end--;

            if (end < 0)
                return string.Empty;

            var start = end;
            while (start > 0 && CalcpadCharacterHelpers.IsIdentifierCharWithDollar(text[start - 1]))
                start--;

            if (start > end)
                return string.Empty;

            var identifier = text.Substring(start, end - start + 1);

            // Verify it starts with a valid identifier start character
            if (identifier.Length > 0 && CalcpadCharacterHelpers.IsIdentifierStartCharWithUnderscore(identifier[0]))
                return identifier;

            return string.Empty;
        }

        /// <summary>
        /// Clears all tracked definitions.
        /// </summary>
        public void Clear()
        {
            _variables.Clear();
            _functions.Clear();
            _macros.Clear();
            _customUnits.Clear();
        }
    }
}
