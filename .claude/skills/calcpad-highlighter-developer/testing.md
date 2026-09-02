# Calcpad Highlighter Testing Guide

## Test Project (Unit Tests)

Highlighter tests are xUnit tests in **`Calcpad.Tests/Highlighter/`** (project: `Calcpad.Tests/Calcpad.Tests.csproj`, .NET 10, xunit 2.9.3). There is no standalone console test runner.

**Running tests:**
```bash
# Every test in the solution
dotnet test Calcpad.Tests/Calcpad.Tests.csproj

# Just the highlighter tests
dotnet test Calcpad.Tests/Calcpad.Tests.csproj --filter "FullyQualifiedName~Calcpad.Tests.Highlighter"

# One test class
dotnet test Calcpad.Tests/Calcpad.Tests.csproj --filter "FullyQualifiedName~SymbolResolverTests"

# One .cpd sample, through the data-driven theory
dotnet test Calcpad.Tests/Calcpad.Tests.csproj --filter "DisplayName~vectors.cpd"
```

## HighlighterLinterFixture

`Calcpad.Tests/Highlighter/HighlighterLinterFixture.cs` is the shared harness, injected via `IClassFixture<HighlighterLinterFixture>`. It exposes `ValidDir`, `ErrorsDir`, an `IncludeFiles` dictionary of every `.cpd` under the test folder (so `#include` resolves in-memory), and:

```csharp
public LinterResult LintFile(string fullPath);
// reads the file → ContentResolver.GetStagedContent(content, IncludeFiles)
// → LintIgnoreRegionParser.ExtractRegions → CalcpadLinter.Lint(staged, ignoreRegions)
```

The `.cpd` files are copied to the output directory by the csproj, so the fixture locates them relative to the assembly.

## Test File Structure

`.cpd` sample files live in two folders and are picked up automatically by data-driven theories — adding a file adds a test case, no registration needed.

- **`Calcpad.Tests/Highlighter/valid/`** — must produce **zero** Error-level diagnostics (`ComprehensiveValidTests`)
- **`Calcpad.Tests/Highlighter/errors/`** — must produce the expected diagnostic codes (`ComprehensiveErrorTests`)

```calcpad
"Test: My Feature"
'Description of what this tests'

x = 5
y = x + 1

z = undefined_var  ' Should trigger CPD-3301
```

## Testing Workflow

1. **Add a `.cpd` sample** to `valid/` (should lint clean) or `errors/` (should produce specific codes), or write a focused xUnit test class for behavior a sample can't express
2. **Run it:**
   ```bash
   dotnet test Calcpad.Tests/Calcpad.Tests.csproj --filter "DisplayName~YourTest.cpd"
   ```
3. **Read the assertion message** — the valid/error theories print the offending code, line, and message
4. **Iterate** on the validator or tokenizer

## Key Test Files

- `Calcpad.Tests/Highlighter/HighlighterLinterFixture.cs` - Shared fixture (resolution + lint pipeline)
- `Calcpad.Tests/Highlighter/ComprehensiveValidTests.cs` - Every `valid/*.cpd` must be error-free
- `Calcpad.Tests/Highlighter/ComprehensiveErrorTests.cs` - Every `errors/*.cpd` must report its codes
- `Calcpad.Tests/Highlighter/SymbolResolverTests.cs` - Cursor → symbol → occurrences
- `Calcpad.Tests/Highlighter/DefinitionClassificationTests.cs`, `MetadataReturnTypeTests.cs`, `ElementWiseFunctionTypeTests.cs` - Type inference
- `Calcpad.Tests/Highlighter/HTMLCommentTests.cs`, `PdfMetadataHighlighterTests.cs`, `SettingsDirectiveHighlighterTests.cs`, `UiDirectiveHighlighterTests.cs` - Metadata comment directives
- `Calcpad.Tests/Highlighter/valid/` - Feature samples, one concern per file
- `Calcpad.Tests/Highlighter/errors/` - Error samples split by CPD code category

## Comprehensive Test Structure

```
Calcpad.Tests/Highlighter/
├── valid/
│   ├── basics.cpd              Scalars, operators, constants, arrow assignment
│   ├── complex_numbers.cpd     Complex literals, arithmetic, functions, phasor
│   ├── vectors.cpd             All vector functions (create, structural, data, find, lookup, math)
│   ├── matrices.cpd            All matrix functions (create, structural, data, lookup, math, decomp, solvers, FFT)
│   ├── functions.cpd           Custom defs, all built-in (trig, hyp, log, rounding, integer, complex, aggregate, conditional)
│   ├── units.cpd               SI, Imperial, dimensionless, angle, electrical, custom units, conversion
│   ├── control_flow.cpd        #if/#else if/#else, #for, #while, #repeat, #break, #continue, nesting
│   ├── macros.cpd              Inline/multiline #def macros, $ params, control flow in macros
│   ├── nested_macros.cpd       Nested macro expansion, and nestedMacro*.cpd include targets
│   ├── commands.cpd            $Root, $Find, $Sup, $Inf, $Area, $Integral, $Slope, $Sum, $Product, $Repeat, $While, $Block, $Inline, $Plot, $Map
│   ├── output_control.cpd      #hide/#show/#pre/#post, #val/#equ/#noc, #nosub/#novar/#varsub, #round, #format, #split/#wrap, #md, #const
│   ├── HTML.cpd                HTML elements, CSS, JavaScript, SVG graphics, macro-generated HTML
│   ├── markdown.cpd            #md regions
│   ├── ui.cpd                  #UI control directives
│   ├── modules.cpd             #include, #local, #global, using imported functions/macros/units
│   ├── include.cpd             Include resolution against import.cpd
│   ├── data_exchange.cpd       #read from, #write to, #append to, @range, TYPE=, SEP=
│   ├── naming.cpd              Greek letters, underscore subscripts, Unicode sub/superscripts, primes, special chars
│   ├── type_inference.cpd      Scalar/vector/matrix return types, HP types, element access, Various type
│   ├── reassignment.cpd        Type changes across reassignment
│   ├── line_continuation.cpd   Explicit _ and implicit ;|&@:({[ continuation, command blocks
│   ├── advanced.cpd            Cross-feature integration: imported macros in loops, units in command blocks, nested macro calls
│   ├── import.cpd              Module target for #include (exports constants, functions, units, macros)
│   └── data.csv                CSV data for #read tests
└── errors/
    ├── include_errors.cpd          CPD-11xx (malformed include, missing filename)
    ├── macro_errors.cpd            CPD-22xx (duplicate, no $, invalid name, nested, unmatched, dup param)
    ├── complex_macro_errors.cpd    CPD-22xx in nested/parameterized macros
    ├── balance_errors.cpd          CPD-31xx (unmatched parens, brackets, braces, control blocks)
    ├── naming_errors.cpd           CPD-32xx (keyword conflict, unit shadow, constant conflict, no params)
    ├── usage_errors.cpd            CPD-33xx (undefined var/func/macro/unit, wrong params, type mismatch)
    ├── reassignment_errors.cpd     CPD-33xx from type changes across reassignment
    ├── semantic_errors.cpd         CPD-34xx (invalid operator, unknown directive, # in command block, incomplete expr)
    └── ui_errors.cpd               #UI directive diagnostics
```

## Testing via Linux Dev Server

For integration testing through the API, use the Linux dev server:

```bash
# Start the dev server (pins port 9420; a bare `dotnet run` gets a random port instead)
Calcpad.Web/backend/scripts/restart-dev-server.sh
```

See [Calcpad.Web/backend/API_SCHEMA.md](../../../Calcpad.Web/backend/API_SCHEMA.md) for full API documentation.

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
      "severity": "error",
      "severityId": 0,
      "source": "Calcpad Linter"
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

The API resolves includes off disk — there is no `includeFiles` payload. Write the included file, then pass `sourceFilePath` so relative paths resolve against its folder:

```bash
mkdir -p /tmp/cpd && echo 'helperFunc(x) = x * 2' > /tmp/cpd/helper.cpd
curl -X POST http://localhost:9420/api/calcpad/lint \
  -H "Content-Type: application/json" \
  -d '{
    "content": "#include helper.cpd\na = helperFunc(5)",
    "sourceFilePath": "/tmp/cpd/main.cpd"
  }'
```

The in-memory `includeFiles` dictionary exists only in the unit-test fixture, via the `ContentResolver.GetStagedContent(content, files)` overload.

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
