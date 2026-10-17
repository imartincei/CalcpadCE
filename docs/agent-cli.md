# CLI automation

Run an existing Calcpad worksheet from a script and retrieve calculated numbers, units and errors as JSON, without opening the desktop app or starting a server. Use `agent evaluate` for one calculation, or `agent sweep` to repeat it with a list of values for one input. This lets you compare design choices or collect data for a plot while keeping the equations in a reusable worksheet.

## Resistor divider tutorial

The runnable files are in the [`Examples/Automation/AgentCli` source directory](https://github.com/imartincei/CalcpadCE/tree/main/Examples/Automation/AgentCli). The worksheet `divider.cpd` contains:

```text
#UI Vin = 12V
#UI R1 = 1kΩ
#UI R2 = 2kΩ
Vout = Vin*R2/(R1 + R2)
```

The example request files use values in the controls' declared units: `"2"` means 2 kΩ for `R2`.

The checked-in `evaluate.json` request is:

```json
{
  "file": "divider.cpd",
  "inputs": { "Vin": "12", "R1": "1", "R2": "2" },
  "outputs": ["Vout"]
}
```

The `sweep.json` request keeps `Vin` and `R1` fixed while trying three values for `R2`:

```json
{
  "file": "divider.cpd",
  "inputs": { "Vin": "12", "R1": "1" },
  "outputs": ["Vout"],
  "sweep": { "variable": "R2", "values": ["1", "2", "3"] }
}
```

Build the CLI from the repository root with the .NET 10 SDK pinned in `global.json`, then run both checked-in requests from the example directory:

```sh
dotnet build Calcpad.Cli/Calcpad.Cli.csproj
cd Examples/Automation/AgentCli
dotnet ../../../Calcpad.Cli/bin/Debug/net10.0/Cli.dll agent evaluate --request evaluate.json
dotnet ../../../Calcpad.Cli/bin/Debug/net10.0/Cli.dll agent sweep --request sweep.json
```

`--request -` reads a UTF-8 JSON document from stdin. Paths supplied to the CLI, including both the request file path and the worksheet's `file` field, resolve relative to the process working directory, not the request file's directory. Worksheet includes and data paths use Calcpad's normal worksheet-relative resolution and path-root rules.

`evaluate.json` sets `Vin` to 12 V, `R1` to 1 kΩ, and `R2` to 2 kΩ, and requests `Vout`. This response excerpt is a valid JSON object; other response fields are omitted:

```json
{
  "status": "ok",
  "values": {
    "Vout": { "real": 8, "imaginary": 0, "isComplex": false, "unit": "V" }
  },
  "errors": []
}
```

The nominal output is 8 V. `sweep.json` keeps `Vin` and `R1` fixed and sets `R2` to 1, 2, then 3 kΩ. The response points include `(x, Vout)` values `(1 kΩ, 6 V)`, `(2 kΩ, 8 V)`, and `(3 kΩ, 9 V)`. Its `sensitivities` excerpt is:

```json
[
  { "output": "Vout", "fromPoint": 0, "toPoint": 1, "deltaOutputPerDeltaInput": 2, "inputUnit": "kΩ", "outputUnit": "V" },
  { "output": "Vout", "fromPoint": 1, "toPoint": 2, "deltaOutputPerDeltaInput": 1, "inputUnit": "kΩ", "outputUnit": "V" }
]
```

These are slopes across the two neighboring intervals, in V/kΩ. The sweep returns ordered data that a caller can use to make a plot; the CLI itself does not draw a plot or open the UI.

## Request format

For example, this valid `sweep` request overrides `R1` and sweeps `R2`. Requests to either command may override only variables declared as scalar `#UI` inputs; unknown names and inputs that do not produce a scalar value are errors.

```json
{
  "file": "divider.cpd",
  "isComplex": false,
  "inputs": { "R1": "1" },
  "outputs": ["Vout"],
  "sweep": { "variable": "R2", "values": ["1", "2", "3"] }
}
```

- `file`: required worksheet path.
- `isComplex`: optional boolean, false by default. Set true for complex arithmetic such as `z = 1 + 2i`.
- `inputs`: optional object mapping case-sensitive `#UI` variable names to string values. Ordinary assignments are not overridable inputs.
- `outputs`: required nonempty list of case-sensitive variable names. Only scalar assignments are supported. Missing, vector or matrix outputs produce calculation errors.
- `sweep`: required for `agent sweep`; one variable and 1–1,000 nonempty string values. More than 1,000 points are rejected before the worksheet is read. A sweep value overrides the same variable in `inputs`.

Input strings follow existing `#UI` control semantics. For `#UI R2 = 2kΩ`, use `"3"` to request 3 kΩ: a numeric control preserves its source unit. Do not append a second unit to a numeric-control override. Controls configured to accept expressions follow their own Calcpad rules.

Macro expansion and includes are prepared once per request. Each sweep point starts with fresh calculation state. The commands disable worksheet `#write` and `#append`, but still allow normal reads and includes. Run trusted worksheets with the permissions and dependencies they need.

## Response and exit codes

Stdout contains one JSON response with `schemaVersion: "1"`. Human-readable execution errors may also appear on stderr. Do not use a successful process launch or a parseable JSON document alone as proof of successful calculation: check the exit code and response errors.

| Exit | Status | Meaning |
| --- | --- | --- |
| 0 | `ok` | All requested calculations and values succeeded. |
| 1 | `error` | Invalid request, file access or execution failure. |
| 2 | `calculation_error` | Worksheet, input or requested-output error. |

`evaluate` returns requested outputs in `values`. A scalar carries `real`, `imaginary`, `isComplex` and `unit`; a unitless value has `unit: null`. `isComplex` is true whenever the imaginary component is nonzero, including pure imaginary values. Values come from the engine before display rounding or HTML formatting. They represent the final captured assignment to each variable, not arbitrary expressions or report text. Unavailable outputs have a null value and an error message. Nonfinite numbers are encoded as JSON strings such as `"NaN"` or `"Infinity"`; consumers must handle these explicitly.

`sweep` returns ordered `points` with the supplied `input` string, evaluated `#UI` scalar `x` (before later ordinary reassignment), requested `values`, and per-point `errors`. The top-level errors summarize failures across the request. Optional fields for the other command may be null. Worksheets that pause for interaction (`#pause` or `#input`) return a calculation error rather than claiming a completed evaluation. Engine diagnostics include source line numbers; request and missing-output errors may have no line number.

## Interval slopes

`sensitivities` contains interval slopes, not point derivatives or normalized percent sensitivities. For each eligible neighboring pair:

```text
slope = (output[next] - output[previous]) / (input[next] - input[previous])
```

`fromPoint` and `toPoint` are zero-based indices into `points`. The input order is preserved. Descending and unevenly spaced inputs are valid. For the divider example the two slopes are 2 and 1 V/kΩ.

A slope is emitted only when both adjacent points succeed, x and y are finite and real, x differs between the points, and their respective unit labels match. Repeated x, failed points, complex values, nonfinite values or differences, or changed units omit that interval. The CLI does not sort, bridge a failed point or convert mismatched units to make a slope. The slope's units are `outputUnit` divided by `inputUnit`.

## Validation and platform scope

The dedicated CLI integration tests exercise the real executable with isolated files and finite test timeouts. Run them from the repository root:

```sh
dotnet test Calcpad.Cli.Tests/Calcpad.Cli.Tests.csproj
```

The existing numerical engine suite remains separate:

```sh
dotnet test Calcpad.Tests/Calcpad.Tests.csproj
```

Platform CI is configured for Windows x64, Linux x64 and Linux ARM64. Configuration does not establish a successful remote run: inspect the PR checks for the actual results. Local validation for this feature was performed on Linux x64. The repository’s [deployment guide](new-selecting-calcpadce-deployment.md) lists macOS support for the VS Code deployment and no macOS support for the desktop app. Neither establishes standalone CLI runtime support; no macOS CLI runtime validation is claimed here.

The 1,000-point limit bounds the number of cases, not the cost of one worksheet. Individual calculations retain the engine's existing execution behavior. No per-request timeout, process sandbox, vector/matrix JSON format, plot renderer, multi-variable sweep or CSV output is introduced by these commands.
