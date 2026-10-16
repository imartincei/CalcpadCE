# Extending the Highlighter

## Adding Function Signatures

Edit FunctionSignatures.cs:

```csharp
// Simple scalar function (element-wise on vectors)
AddScalarFunction("newFunc", 1, "Description", isElementWise: true);

// Function with specific parameter types
AddFunction("newFunc",
    minParams: 2, maxParams: 3,
    new[] { ParameterType.Vector, ParameterType.Scalar, ParameterType.Integer },
    CalcpadType.Vector, "Description");
```

Also add to CalcpadBuiltIns.cs Functions set if not already there.

## Creating Validators

```csharp
namespace Calcpad.Highlighter.Linter.Validators.Stage3
{
    public class NewValidator
    {
        public static List<LinterDiagnostic> Validate(
            Stage3Context context,
            TokenizedLineProvider tokenProvider)
        {
            var diagnostics = new List<LinterDiagnostic>();
            for (int i = 0; i < context.Lines.Count; i++)
            {
                var tokens = tokenProvider.GetTokensForLine(i);
                if (tokenProvider.IsCommentLine(i)) continue;

                foreach (var token in tokens)
                {
                    if (/* error condition */)
                    {
                        diagnostics.Add(new LinterDiagnostic
                        {
                            Line = i, Column = token.Column,
                            EndColumn = token.Column + token.Length,
                            Code = ErrorCodes.YourErrorCode,
                            Message = "Your error message",
                            Severity = LinterSeverity.Error
                        });
                    }
                }
            }
            return diagnostics;
        }
    }
}
```

Register in CalcpadLinter.cs:
```csharp
diagnostics.AddRange(NewValidator.Validate(stage3Context, tokenProvider));
```

## Type Inference

TypeTracker infers types from expressions. Key patterns:

```csharp
// Functions that return vectors
private static readonly HashSet<string> VectorReturningFunctions = new(StringComparer.OrdinalIgnoreCase)
{
    "vector", "range", "range_hp", "sort", "rsort", "reverse",
    "unit", "cross", "row", "col", "diag2vec", /* ... */
};

// Functions that return matrices
private static readonly HashSet<string> MatrixReturningFunctions = new(StringComparer.OrdinalIgnoreCase)
{
    "matrix", "identity", "diagonal", "transp", "inverse",
    "vec2diag", "vec2col", "vec2row", /* ... */
};
```

To extend type inference, modify TypeTracker.InferTypeFromExpression().
