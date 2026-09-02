# Calcpad.Core Architecture & Testing Reference

## Directory Structure

```
Calcpad.Core/
├── BaseTypes/
│   ├── IValue.cs          - Common interface for scalar/vector/matrix values
│   ├── IScalarValue.cs    - Real/complex scalar abstraction
│   ├── RealValue.cs       - Real scalar (value + units)
│   ├── ComplexValue.cs    - Complex scalar
│   ├── Complex.cs         - Complex arithmetic primitive
│   ├── Parameter.cs       - Function parameters
│   ├── Variable.cs        - Variable storage
│   └── Unit.cs            - Unit of measurement (internal)
├── Calculator/
│   ├── Calculator.cs      - Abstract base (internal)
│   ├── RealCalculator.cs  - Real number operations
│   ├── ComplexCalculator.cs - Complex operations
│   ├── MatrixCalculator.cs  - Matrix operations
│   └── VectorCalculator.cs  - Vector operations
├── Matrix/
│   ├── Matrix.cs          - Base matrix type
│   ├── ColumnMatrix.cs    - Column vector as matrix
│   ├── DiagonalMatrix.cs  - Diagonal matrix
│   ├── SymmetricMatrix.cs - Symmetric matrix
│   ├── LowerTriangularMatrix.cs / UpperTriangularMatrix.cs
│   └── HpMatrix/, hpMatrix/ - High-performance (unitless) matrix variants
├── Vector/
│   ├── Vector.cs          - Base vector type
│   ├── ColumnVector.cs / RowVector.cs
│   ├── LargeVector.cs     - Sparse/large storage
│   └── HpVector.cs        - High-performance (unitless) vector
├── Parsers/
│   ├── MathParser/        - Main expression parser
│   ├── ExpressionParser/  - Document-level parsing (directives, output, HtmlResult)
│   ├── MacroParser.cs     - #include and #def macro expansion
│   ├── PlotParser.cs, ChartParser.cs, MapParser.cs - $Plot / $Map parsing
│   └── UnitsParser.cs     - Unit expression parsing
├── Plotter/
│   ├── Plotter.cs         - 2D plotting engine
│   ├── MapPlotter.cs      - 2D color map plotting
│   ├── ChartPlotter.cs    - Chart generation
│   └── SvgDrawing.cs, SvgPoint.cs - Vector output
├── Output/
│   ├── OutputWriter.cs    - Abstract result formatting
│   └── HtmWriter.cs, TextWriter.cs, XmlWriter.cs - Per-target writers
├── Solver.cs              - Iterative/numerical methods behind $Root, $Find, $Integral, ...
├── Settings.cs            - Settings / MathSettings / PlotSettings
├── CalcpadError.cs, Exceptions.cs, MathParserException.cs - Error types
├── PathRoots.cs           - {project} / {library} / {user} resolution
├── ImageReferences.cs, Container.cs, DirectiveDto.cs, ExtensionMethods.cs
└── Messages.resx (+ .bg, .zh) - Localized diagnostic messages

## Key Classes

### MathParser
The core expression parser. Handles:
- Tokenizing mathematical expressions
- Building expression trees
- Evaluating with proper operator precedence
- Managing variables and functions
- Unit conversions

### Calculator Classes
`Calculator` is `internal abstract` and dispatches by operator/function *index*, not by name — name resolution happens in the parser:

```csharp
internal abstract class Calculator
{
    internal abstract IScalarValue EvaluateOperator(long index, in IScalarValue a, in IScalarValue b);
    internal abstract IScalarValue EvaluateFunction(long index, in IScalarValue a);
    internal abstract IScalarValue EvaluateFunction2(long index, in IScalarValue a, in IScalarValue b);
    internal abstract IValue EvaluateFunction3(long index, in IValue a, in IValue b, in IValue c);
    internal abstract IScalarValue EvaluateMultiFunction(long index, IScalarValue[] a);
    internal abstract IScalarValue EvaluateInterpolation(long index, IScalarValue[] a);
}

// RealCalculator - sin, cos, tan, log, exp, sqrt, etc.
// ComplexCalculator - re(), im(), phase(), conj(), etc.
// MatrixCalculator - det(), inverse(), transp(), eigenvals(), etc.
// VectorCalculator - norm(), dot(), cross(), sort(), etc.
```

### Unit System
`Unit` is `internal` and holds a per-dimension exponent/factor pair rather than a single scale plus an integer dimension vector — it carries a `double[] _factors` alongside the dimension powers, so non-integer exponents and scaled bases (kg, not g) work. Read `BaseTypes/Unit.cs` before changing unit arithmetic; do not assume the simplified `{ Name, Factor, int[] Dimensions }` shape.

## Matrix Type Hierarchy

```
Matrix (base)
├── ColumnMatrix      - m×1 matrix (column vector)
├── DiagonalMatrix    - Only diagonal elements stored
├── SymmetricMatrix   - A = A^T, stores upper triangle
├── LowerTriangularMatrix
├── UpperTriangularMatrix
└── (Regular dense matrix)
```

## Plotting System

```csharp
Plotter.Plot(function, xMin, xMax, options);
Plotter.PlotParametric(xFunc, yFunc, tMin, tMax, options);
MapPlotter.Plot(function, xMin, xMax, yMin, yMax, options);
```

Uses SkiaSharp for rendering to PNG/SVG.

## External Dependencies

- **Markdig.Signed** (0.43.0) - Markdown processing in comments
- **SkiaSharp** (3.119.1) - Graphics rendering for plots
- **System.IO.Packaging** (10.0.0) - Package handling

## Calcpad Syntax Reference

### Operators (precedence high to low)
1. `!` - Factorial
2. `^` - Exponentiation
3. `*`, `/`, `\` (integer div), `%%` (modulo)
4. `+`, `-`
5. Comparison: `==`, `!=`, `<`, `>`, `<=`, `>=`
6. Logical: `&&`, `||`, `^^`

### Built-in Functions (partial list)
**Trigonometric:** sin, cos, tan, csc, sec, cot, asin, acos, atan, atan2
**Hyperbolic:** sinh, cosh, tanh, asinh, acosh, atanh
**Logarithmic:** log, ln, log_2, exp
**Roots:** sqr/sqrt, cbrt, root(x,n)
**Rounding:** round, floor, ceiling, trunc
**Complex:** re, im, abs, phase, conj
**Aggregate:** min, max, sum, average, product
**Vector:** vector(n), range(a,b,step), len, sort, reverse, dot, cross, norm
**Matrix:** matrix(m,n), identity(n), det, inverse, transp, eigenvals, lsolve

### Commands (iterative/numerical methods)
`$Root`, `$Find`, `$Sup`, `$Inf` - Optimization
`$Area`, `$Integral`, `$Slope` - Calculus
`$Sum`, `$Product`, `$Repeat` - Iteration
`$Plot`, `$Map` - Visualization

## Testing

### Unit Tests
```bash
dotnet test Calcpad.Tests/Calcpad.Tests.csproj

# One area
dotnet test Calcpad.Tests/Calcpad.Tests.csproj --filter "FullyQualifiedName~Matrices"
```

xUnit. Suites: `ExpressionParser/`, `Scalars/`, `Vectors/`, `Matrices/`, `Macros/`, `Filepaths/`, `UI/`, `Server/`, `Highlighter/`.

### Integration Testing via Linux Dev Server
```bash
# Start the dev server (pins port 9420; a bare `dotnet run` gets a random port instead)
Calcpad.Web/backend/scripts/restart-dev-server.sh
```

See [Calcpad.Web/backend/API_SCHEMA.md](../../../Calcpad.Web/backend/API_SCHEMA.md) for full API documentation.

**POST /api/calcpad/convert** - Test calculations:
```bash
curl -X POST http://localhost:9420/api/calcpad/convert \
  -H "Content-Type: application/json" \
  -d '{"content": "x = 5*m\ny = sin(45°)", "theme": "light"}'
```

**Check for errors:**
```bash
curl -s -X POST http://localhost:9420/api/calcpad/convert \
  -H "Content-Type: application/json" \
  -d '{"content": "your test code"}' | grep -i "class=\"err"
```
