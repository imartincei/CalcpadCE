# PyCalcpad API Surface Reference

Everything below is the actual surface of `Calcpad.Api/PyCalcpad`. The namespace is `PyCalcpad`, and its settings classes deliberately shadow the `Calcpad.Core` ones — the wrapper's job is to expose plain, serializable settings and convert them at the boundary.

## Calculator Class

Expression-level evaluation. Wraps `Calcpad.Core.MathParser` directly.

```csharp
public class Calculator
{
    public Calculator(MathSettings settings);          // no parameterless ctor

    public string Eval(string code);                   // parse + calculate, returns ResultAsString
    public string Run(string code);                    // parse + calculate, returns parser.ToString()
    public void SetVariable(string name, double value);
}
```

`Eval` also assigns the result to the variable `ans`, so the next call can chain off it. There is no `GetVariable` and no `Clear` — a fresh `Calculator` is the way to reset state.

## Parser Class

Document-level parsing and file conversion. Wraps `Calcpad.Core.ExpressionParser` plus `MacroParser` for `#include`/macro expansion.

```csharp
public class Parser
{
    public Settings Settings;                          // public field, not a property

    public string Parse(string code);                  // returns HtmlResult
    public bool Convert(string inputFileName, string outputFileName);
}
```

`Convert` chooses its output by extension. `outputFileName` may be a full path, empty (defaults to `.html` beside the input), or just `"html"` / `"htm"` / `"docx"` / `"pdf"` to mean "same name, this extension". Anything else returns `false`. It sets the process's current directory to the input file's folder so relative `#include` and `#read` paths resolve. When macro expansion fails, it writes a syntax-highlighted listing of the unwrapped code instead of a report, and still returns `true`.

## Settings Classes

```csharp
public class Settings
{
    public MathSettings Math { get; set; } = new();
    public PlotSettings Plot { get; set; } = new();
    public string Units { get; set; } = "m";           // "m" | "i" | "u"
}

public class MathSettings
{
    public int Decimals { get; set; }                  // clamped to 0..15, default 2
    public int Degrees { get; set; }                   // TrigUnits: Deg=0, Rad=1, Grad=2. Default Deg
    public bool IsComplex { get; set; }                // default false
    public bool Substitute { get; set; }               // default true
    public bool FormatEquations { get; set; }          // default true
    public bool ZeroSmallMatrixElements { get; set; }  // default true
    public int MaxOutputCount { get; set; }            // clamped to 5..100, default 20

    public enum TrigUnits { Deg, Rad, Grad }
}

public class PlotSettings
{
    public bool IsAdaptive { get; set; }               // default true
    public double ScreenScaleFactor { get; set; } = 2.0;
    public string ImagePath { get; set; }
    public string ImageUri { get; set; }
    public bool VectorGraphics { get; set; }           // default false
    public int ColorScale { get; set; }                // ColorScales, default Rainbow
    public bool SmoothScale { get; set; }
    public bool Shadows { get; set; }                  // getter also depends on ColorScale
    public LightDirections LightDirection { get; set; } // default NorthWest

    public enum LightDirections { North, NorthEast, East, SouthEast, South, SouthWest, West, NorthWest }
    public enum ColorScales { None, Gray, Rainbow, Terrain, VioletToYellow, GreenToYellow, Blues }
}
```

`Decimals` and `MaxOutputCount` clamp in their setters rather than throwing — an out-of-range value is silently pinned to the nearest bound.

`Shadows` is not a plain auto-property: its getter returns false for the `Gray` scale even when set true, and true for `None` regardless. Read it back rather than assuming what you wrote.

## Internal Types

Not part of the public API — they exist to serve `Parser.Convert`:

- **`Converter`** (`internal`) — renders `HtmlResult` into a file: `ToHtml`, `ToOpenXml` (via `Calcpad.OpenXml.OpenXmlWriter`), `ToPdf`. It applies the HTML worksheet template from `doc/`.
- **`Reader`** (`internal static`) — `Read`, `Include`, `CodeToHtml`. File loading and the highlighted-source fallback listing.
- **`Program`** — the console entry point; `Program.AppPath` is what `Converter` resolves its template and assets against.

## Usage Examples

### Expression evaluation
```csharp
var calc = new Calculator(new MathSettings { Decimals = 4 });
var result = calc.Eval("2 + 2");            // "4"

calc.SetVariable("x", 5);
calc.SetVariable("y", 3);
result = calc.Eval("x^2 + y^2");            // "34"
result = calc.Eval("ans/2");                // chains off the previous result
```

### Rendering a document
```csharp
var parser = new Parser
{
    Settings = new Settings
    {
        Math = new MathSettings { Decimals = 4, Degrees = (int)MathSettings.TrigUnits.Rad },
        Units = "m"
    }
};

string html = parser.Parse("a = 5m\nb = 3m\narea = a*b");
```

### Converting a file
```csharp
var parser = new Parser { Settings = new Settings() };

parser.Convert("calcs/beam.cpd", "pdf");             // → calcs/beam.pdf
parser.Convert("calcs/beam.cpd", "");                // → calcs/beam.html
parser.Convert("calcs/beam.cpd", "out/report.docx"); // explicit path
```

## Error Handling

There is no `CalculationException`. Calculation errors are **rendered into the output**, not thrown: `ExpressionParser` writes them into `HtmlResult` as `<span class="err">` spans, and `MathParser` errors surface in the result string. To detect failure, inspect the returned HTML — the same convention the web backend and every editor use.

`Parser.Convert` returns `false` only for an unrecognized output extension. Missing files and IO errors propagate as ordinary .NET exceptions.

## Integration with Core

The wrapper is a thin translation layer. Each PyCalcpad settings class is converted to its `Calcpad.Core` counterpart at the point of use:

```csharp
// Calculator.cs — shared by Parser via Calculator.ConvertMathSettings
internal static Calcpad.Core.MathSettings ConvertMathSettings(MathSettings settings) =>
    new()
    {
        Decimals = settings.Decimals,
        Degrees = settings.Degrees,
        IsComplex = settings.IsComplex,
        Substitute = settings.Substitute,
        FormatEquations = settings.FormatEquations,
        ZeroSmallMatrixElements = settings.ZeroSmallMatrixElements,
        MaxOutputCount = settings.MaxOutputCount,
    };
```

When you add a setting, it must be added in three places: the PyCalcpad class, the `Convert*Settings` mapper, and the `Calcpad.Core` counterpart. A field added to only the first is silently ignored.

`Parser.Convert` shows the full document pipeline the wrapper exposes: `Reader.Read` → `MacroParser.Parse` (includes + macros, carrying `PathRoots`) → `ExpressionParser.Parse` → `Converter`.
