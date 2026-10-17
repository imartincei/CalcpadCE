using System.Text.Json;
using Xunit;

namespace Calcpad.Cli.Tests;

public sealed partial class AgentCliProcessTests
{
    [Fact]
    public async Task StdinAcceptsRawUtf8UnicodeRatherThanRequiringEscapedJson()
    {
        using var sandbox = new Sandbox("raw Unicode Ω μ");
        sandbox.WriteWorksheet("#UI L = 2μm\n");
        var request = JsonSerializer.Serialize(
            new { file = sandbox.WorksheetPath, inputs = new { L = "3" }, outputs = new[] { "L" } },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.Contains("Ω", request);

        var result = await RunAsync(sandbox, "evaluate", "-", request);

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Equal(3, Value(json, "L").GetProperty("real").GetDouble());
        Assert.Equal("μm", Value(json, "L").GetProperty("unit").GetString());
    }

    [Fact]
    public async Task FiniteCoordinatesWithOverflowingDifferenceDoNotProduceFalseZeroSlope()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI {\"allowExpression\":true} x = 1\ny = sign(x)\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            outputs = new[] { "y" },
            sweep = new { variable = "x", values = new[] { "-10^308", "10^308" } }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        var points = json.RootElement.GetProperty("points").EnumerateArray().ToArray();
        Assert.All(points, p => Assert.True(double.IsFinite(p.GetProperty("x").GetProperty("real").GetDouble())));
        Assert.Equal(new[] { -1d, 1d }, points.Select(p => Value(p, "y").GetProperty("real").GetDouble()));
        Assert.Empty(json.RootElement.GetProperty("sensitivities").EnumerateArray());
    }

    [Fact]
    public async Task PureImaginaryOutputIsComplexAndNeverProducesRealSlope()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI x = 1\ny = x*1i\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            isComplex = true,
            outputs = new[] { "y" },
            sweep = new { variable = "x", values = new[] { "1", "2" } }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.True(Value(json.RootElement.GetProperty("points")[0], "y").GetProperty("isComplex").GetBoolean());
        Assert.Equal(2, Value(json.RootElement.GetProperty("points")[1], "y").GetProperty("imaginary").GetDouble());
        Assert.Empty(json.RootElement.GetProperty("sensitivities").EnumerateArray());
    }

    [Fact]
    public async Task PureImaginaryInputDoesNotProduceARealSlope()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI {\"allowExpression\":true} x = 1\ny = re(x)\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            isComplex = true,
            outputs = new[] { "y" },
            sweep = new { variable = "x", values = new[] { "1", "2i" } }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.True(json.RootElement.GetProperty("points")[1].GetProperty("x").GetProperty("isComplex").GetBoolean());
        Assert.Empty(json.RootElement.GetProperty("sensitivities").EnumerateArray());
    }

    [Fact]
    public async Task DuplicateOutputNamesProduceOneValueAndOneSlopePerInterval()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI x = 1\ny = x^2\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            outputs = new[] { "y", "y" },
            sweep = new { variable = "x", values = new[] { "1", "2" } }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Single(json.RootElement.GetProperty("points")[0].GetProperty("values").EnumerateObject());
        var slope = Assert.Single(json.RootElement.GetProperty("sensitivities").EnumerateArray());
        Assert.Equal(3, slope.GetProperty("deltaOutputPerDeltaInput").GetDouble());
    }

    [Fact]
    public async Task EvaluateAppliesNamedInputMagnitudeAndPreservesDeclaredUnit()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI R2 = 2kΩ\nVout = 12V*R2/(1kΩ + R2)\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            inputs = new { R2 = "3" },
            outputs = new[] { "R2", "Vout" }
        };

        var result = await RunAsync(sandbox, "evaluate", "-", JsonSerializer.Serialize(request));

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Equal(3, Value(json, "R2").GetProperty("real").GetDouble());
        Assert.Equal("kΩ", Value(json, "R2").GetProperty("unit").GetString());
        Assert.Equal(9, Value(json, "Vout").GetProperty("real").GetDouble());
    }

    [Fact]
    public async Task UnitAwareDividerSweepReportsMagnitudesAndSlopeUnits()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI R2 = 1kΩ\nVout = 12V*R2/(1kΩ + R2)\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            outputs = new[] { "Vout" },
            sweep = new { variable = "R2", values = new[] { "1", "2", "3" } }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        var points = json.RootElement.GetProperty("points").EnumerateArray().ToArray();
        Assert.Equal(new[] { 6d, 8d, 9d }, points.Select(p => Value(p, "Vout").GetProperty("real").GetDouble()));
        Assert.All(points, p => Assert.Equal("kΩ", p.GetProperty("x").GetProperty("unit").GetString()));
        var slopes = json.RootElement.GetProperty("sensitivities").EnumerateArray().ToArray();
        Assert.Equal(new[] { 2d, 1d }, slopes.Select(s => s.GetProperty("deltaOutputPerDeltaInput").GetDouble()));
        Assert.All(slopes, s =>
        {
            Assert.Equal("kΩ", s.GetProperty("inputUnit").GetString());
            Assert.Equal("V", s.GetProperty("outputUnit").GetString());
        });
    }

    [Fact]
    public async Task SweepOmitsSlopesWhenInputUnitLabelsChange()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI {\"type\":\"dropdown\",\"keys\":[\"a\",\"b\"],\"values\":[\"1m\",\"2cm\"]} x = 1m\ny = 2m\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            outputs = new[] { "y" },
            sweep = new { variable = "x", values = new[] { "1m", "2cm" } }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        var points = json.RootElement.GetProperty("points");
        Assert.Equal("m", points[0].GetProperty("x").GetProperty("unit").GetString());
        Assert.Equal("cm", points[1].GetProperty("x").GetProperty("unit").GetString());
        Assert.Empty(json.RootElement.GetProperty("sensitivities").EnumerateArray());
    }

    [Fact]
    public async Task SweepOmitsSlopesForComplexInputsEvenWithRealOutputs()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI {\"allowExpression\":true} x = 1\ny = re(x)\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            isComplex = true,
            outputs = new[] { "y" },
            sweep = new { variable = "x", values = new[] { "1", "2+1i" } }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.True(json.RootElement.GetProperty("points")[1].GetProperty("x").GetProperty("isComplex").GetBoolean());
        Assert.Empty(json.RootElement.GetProperty("sensitivities").EnumerateArray());
    }

    [Fact]
    public async Task SweepOmitsSlopesForNonfiniteInputsEvenWithFiniteOutputs()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI {\"allowExpression\":true} x = 1\ny = 2\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            outputs = new[] { "y" },
            sweep = new { variable = "x", values = new[] { "1", "1/0" } }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Equal("Infinity", json.RootElement.GetProperty("points")[1].GetProperty("x").GetProperty("real").GetString());
        Assert.Empty(json.RootElement.GetProperty("sensitivities").EnumerateArray());
    }

    [Fact]
    public async Task CompositeInputOverrideIsRejectedEvenWhenItsUiDeclarationMatches()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI v = [1; 2]\ny = 4\n");
        var request = new { file = sandbox.WorksheetPath, inputs = new { v = "[3; 4]" }, outputs = new[] { "y" } };

        var result = await RunAsync(sandbox, "evaluate", "-", JsonSerializer.Serialize(request));

        Assert.Equal(2, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Contains("Input 'v' did not produce a scalar value.", json.RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task ScalarOutputReassignedToVectorIsUnavailableRatherThanStale()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("x = 1\nx = [2; 3]\n");
        var request = new { file = sandbox.WorksheetPath, outputs = new[] { "x" } };

        var result = await RunAsync(sandbox, "evaluate", "-", JsonSerializer.Serialize(request));

        Assert.Equal(2, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Equal(JsonValueKind.Null, Value(json, "x").ValueKind);
    }

    [Fact]
    public async Task UnknownAgentCommandReturnsUsageAsAJsonError()
    {
        using var sandbox = new Sandbox();

        var result = await RunAsync(sandbox, "unknown-command", "-", "{}");

        Assert.Equal(1, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Equal("error", json.RootElement.GetProperty("status").GetString());
        Assert.Contains("Usage:", result.Stderr);
    }

    [Fact]
    public async Task MissingRequestFileReturnsOneJsonError()
    {
        using var sandbox = new Sandbox();

        var result = await RunAsync(sandbox, "evaluate", sandbox.PathFor("missing request.json"));

        Assert.Equal(1, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Equal("error", json.RootElement.GetProperty("status").GetString());
        Assert.NotEmpty(result.Stderr);
    }

    [Fact]
    public async Task EngineErrorsRetainOneBasedSourceLineNumbers()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("a = 1\nb = unknownFunction(2)\n");
        var request = new { file = sandbox.WorksheetPath, outputs = new[] { "b" } };

        var result = await RunAsync(sandbox, "evaluate", "-", JsonSerializer.Serialize(request));

        Assert.Equal(2, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Contains(json.RootElement.GetProperty("errors").EnumerateArray(), e => e.GetString()!.StartsWith("line 2:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RelativeWorksheetPathUsesWorkingDirectoryRatherThanRequestDirectory()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("y = 4\n");
        var requestDirectory = sandbox.PathFor("requests");
        Directory.CreateDirectory(requestDirectory);
        var requestFile = Path.Combine(requestDirectory, "request.json");
        await File.WriteAllTextAsync(requestFile, JsonSerializer.Serialize(new { file = "worksheet.cpd", outputs = new[] { "y" } }));

        var result = await RunAsync(sandbox, "evaluate", requestFile);

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Equal(4, Value(json, "y").GetProperty("real").GetDouble());
    }

    [Fact]
    public async Task DuplicateInputsOmitOnlyTheirIntervalAndPreserveOriginalPointIndices()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI x = 0\ny = x^2\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            outputs = new[] { "y" },
            sweep = new { variable = "x", values = new[] { "0", "1", "1", "3" } }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        var slopes = json.RootElement.GetProperty("sensitivities").EnumerateArray().ToArray();
        Assert.Equal(new[] { 1d, 4d }, slopes.Select(s => s.GetProperty("deltaOutputPerDeltaInput").GetDouble()));
        Assert.Equal(new[] { 0, 2 }, slopes.Select(s => s.GetProperty("fromPoint").GetInt32()));
        Assert.Equal(new[] { 1, 3 }, slopes.Select(s => s.GetProperty("toPoint").GetInt32()));
    }
}
