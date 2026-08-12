# Calcpad Highlighter Testing Guide

## Test Project (Unit Tests)

The test project is located at `Calcpad.Highlighter/Tests/Tests.csproj`.

**Running tests:**
```bash
# Run all sample tests (Samples/ folder)
dotnet run --project Calcpad.Highlighter/Tests/Tests.csproj

# Run a single sample file
dotnet run --project Calcpad.Highlighter/Tests/Tests.csproj -- --file your_test.cpd

# Or with Samples/ prefix (both work)
dotnet run --project Calcpad.Highlighter/Tests/Tests.csproj -- --file Samples/your_test.cpd

# Run all comprehensive tests
dotnet run --project Calcpad.Highlighter/Tests/Tests.csproj -- --folder comprehensive

# Run only error tests
dotnet run --project Calcpad.Highlighter/Tests/Tests.csproj -- --folder comprehensive/errors

# Run a single comprehensive test file
dotnet run --project Calcpad.Highlighter/Tests/Tests.csproj -- --file comprehensive/basics.cpd

# Output is written to test-output.log in the target folder
```

**Notes:**
- The test runner automatically finds folders relative to the assembly location
- You can run tests from anywhere in the solution - no need to cd into specific directories
- When using `--file`, you can provide just the filename or include the folder prefix
- When using `--folder`, only top-level .cpd files in that folder are run (not subdirectories)

The test runner is an executable that runs all sample files through the linter and reports diagnostics. It is NOT an xUnit test project - it's a console app that exercises the linter.

## Using LinterTestRunner

The `LinterTestRunner` class in `Calcpad.Highlighter/Tests/LinterTestRunner.cs` is the main test harness.

```csharp
// The runner will:
// 1. Load all .cpd files from Tests/Samples/
// 2. Run through all 3 content resolution stages
// 3. Tokenize
// 4. Lint
// 5. Output diagnostics with line mappings to test-output.log
```

## Test File Structure

Create .cpd files in `Calcpad.Highlighter/Tests/Samples/`:

```calcpad
"Test: My Feature"
'Description of what this tests'

' Valid cases
x = 5
y = x + 1

' Error cases (comments describe expected errors)
z = undefined_var  ' Should trigger CPD-3301
```

## Testing Workflow

When testing new features or bug fixes:

1. **Create a test .cpd file** in `Calcpad.Highlighter/Tests/Samples/` with the specific syntax you're testing
2. **Run the test file individually** to see detailed output:
   ```bash
   dotnet run --project Calcpad.Highlighter/Tests/Tests.csproj -- --file YourTest.cpd
   ```
3. **Check the output** for token types, diagnostics, and any errors
4. **Iterate** on your implementation based on the test results

## Key Test Files

- `Calcpad.Highlighter/Tests/LinterTestRunner.cs` - Main test harness (entry point)
- `Calcpad.Highlighter/Tests/TestFileProvider.cs` - Loads test files
- `Calcpad.Highlighter/Tests/QuickTest.cs` - Quick ad-hoc testing
- `Calcpad.Highlighter/Tests/Samples/` - Ad-hoc / targeted test .cpd files
- `Calcpad.Highlighter/Tests/comprehensive/` - Comprehensive feature tests (one test per line)
- `Calcpad.Highlighter/Tests/comprehensive/errors/` - Error tests split by CPD code category

## Comprehensive Test Structure

```
comprehensive/
├── basics.cpd              Scalars, operators, constants, arrow assignment
├── complex_numbers.cpd     Complex literals, arithmetic, functions, phasor
├── vectors.cpd             All vector functions (create, structural, data, find, lookup, math)
├── matrices.cpd            All matrix functions (create, structural, data, lookup, math, decomp, solvers, FFT)
├── functions.cpd           Custom defs, all built-in (trig, hyp, log, rounding, integer, complex, aggregate, conditional)
├── units.cpd               SI, Imperial, dimensionless, angle, electrical, custom units, conversion
├── control_flow.cpd        #if/#else if/#else, #for, #while, #repeat, #break, #continue, nesting
├── macros.cpd              Inline/multiline #def macros, $ params, control flow in macros
├── commands.cpd            $Root, $Find, $Sup, $Inf, $Area, $Integral, $Slope, $Sum, $Product, $Repeat, $While, $Block, $Inline, $Plot, $Map
├── output_control.cpd      #hide/#show/#pre/#post, #val/#equ/#noc, #nosub/#novar/#varsub, #round, #format, #split/#wrap, #md, #const
├── HTML.cpd                HTML elements, CSS, JavaScript, SVG graphics, macro-generated HTML
├── modules.cpd             #include, #local, #global, using imported functions/macros/units
├── data_exchange.cpd       #read from, #write to, #append to, @range, TYPE=, SEP=
├── naming.cpd              Greek letters, underscore subscripts, Unicode sub/superscripts, primes, special chars
├── type_inference.cpd      Scalar/vector/matrix return types, HP types, element access, Various type
├── line_continuation.cpd   Explicit _ and implicit ;|&@:({[ continuation, command blocks
├── advanced.cpd            Cross-feature integration: imported macros in loops, units in command blocks, nested macro calls
├── import.cpd              Module target for #include (exports constants, functions, units, macros)
├── data.csv                CSV data for #read tests
└── errors/
    ├── include_errors.cpd  CPD-11xx (malformed include, missing filename)
    ├── macro_errors.cpd    CPD-22xx (duplicate, no $, invalid name, nested, unmatched, dup param)
    ├── balance_errors.cpd  CPD-31xx (unmatched parens, brackets, braces, control blocks)
    ├── naming_errors.cpd   CPD-32xx (keyword conflict, unit shadow, constant conflict, no params)
    ├── usage_errors.cpd    CPD-33xx (undefined var/func/macro/unit, wrong params, type mismatch)
    └── semantic_errors.cpd CPD-34xx (invalid operator, unknown directive, # in command block, incomplete expr)
```

## Testing via Linux Dev Server

For integration testing through the API, use the Linux dev server:

```bash
# Start the dev server (runs on port 9420)
./scripts/Calcpad.Server/restart-dev-server.sh
```

See [Calcpad.Server/API_SCHEMA.md](../../Calcpad.Server/API_SCHEMA.md) for full API documentation.

### Linter Endpoint

**POST /api/calcpad/lint** - Get diagnostics for Calcpad code:
```bash
curl -X POST http://localhost:9420/api/calcpad/lint \
  -H "Content-Type: application/json" \
  -d '{"content": "a = undefined_var\nb = sin()"}'
```

Response includes error counts and diagnostics:
```json
{
  "errorCount": 2,
  "warningCount": 0,
  "diagnostics": [
    {
      "line": 0,
      "column": 4,
      "endColumn": 17,
      "code": "CPD-3301",
      "message": "Undefined variable: 'undefined_var'",
      "severity": "error"
    }
  ]
}
```

### Highlight Endpoint

**POST /api/calcpad/highlight** - Get syntax tokens:
```bash
curl -X POST http://localhost:9420/api/calcpad/highlight \
  -H "Content-Type: application/json" \
  -d '{"content": "x = sin(45)*m", "includeText": true}'
```

### Definitions Endpoint

**POST /api/calcpad/definitions** - Get macros, functions, variables:
```bash
curl -X POST http://localhost:9420/api/calcpad/definitions \
  -H "Content-Type: application/json" \
  -d '{"content": "#def double$(x$) = 2*x$\nf(a; b) = a + b\nvec = [1; 2; 3]"}'
```

### Testing with Include Files

```bash
curl -X POST http://localhost:9420/api/calcpad/lint \
  -H "Content-Type: application/json" \
  -d '{
    "content": "#include helper.cpd\na = helperFunc(5)",
    "includeFiles": {
      "helper.cpd": "helperFunc(x) = x * 2"
    }
  }'
```

### Convert Endpoint (Runtime Validation)

**POST /api/calcpad/convert** - Run code through Calcpad.Core and get HTML output. Use this to verify that test .cpd files are valid Calcpad code (not just linter-clean):
```bash
# Inline test
curl -s -X POST http://localhost:9420/api/calcpad/convert \
  -H "Content-Type: application/json" \
  -d '{"content": "a = 5\nb = a + 1"}' | grep '<span class="err">'

# Test a .cpd file (pipe through python to JSON-escape)
content=$(cat path/to/test.cpd | python3 -c "import sys,json; print(json.dumps(sys.stdin.read()))")
curl -s http://localhost:9420/api/calcpad/convert -X POST \
  -H "Content-Type: application/json" \
  -d "{\"content\": $content}" | grep '<span class="err">'
```

Runtime errors appear as `<span class="err">` in the HTML output. No output from grep means the code is valid.

### Check Diagnostics with jq

```bash
# Pretty-print diagnostics
curl -s -X POST http://localhost:9420/api/calcpad/lint \
  -H "Content-Type: application/json" \
  -d '{"content": "a = b"}' | jq '.diagnostics'

# Count errors
curl -s -X POST http://localhost:9420/api/calcpad/lint \
  -H "Content-Type: application/json" \
  -d '{"content": "a = b"}' | jq '.errorCount'
```
