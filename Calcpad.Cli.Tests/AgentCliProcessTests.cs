using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Calcpad.Cli.Tests;

public sealed partial class AgentCliProcessTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task EvaluateReadsRequestFileAndEmitsOnlyJsonWithUnicodeUnitsAndSpacedPaths()
    {
        using var sandbox = new Sandbox("worksheet with spaces Ω");
        sandbox.WriteWorksheet("#UI L = 2μm\nA = 3m^2\nV = 8V\n");
        var requestPath = sandbox.WriteRequest(new
        {
            file = sandbox.WorksheetPath,
            outputs = new[] { "L", "A", "V" }
        });

        var result = await RunAsync(sandbox, "evaluate", requestPath);

        Assert.True(result.ExitCode == 0, $"CLI exit code {result.ExitCode}. stdout: {result.Stdout}\nstderr: {result.Stderr}");
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Equal("1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("μm", Value(json, "L").GetProperty("unit").GetString());
        Assert.Equal("m^2", Value(json, "A").GetProperty("unit").GetString());
        Assert.Equal("V", Value(json, "V").GetProperty("unit").GetString());
        Assert.Equal(8, Value(json, "V").GetProperty("real").GetDouble());
    }

    [Fact]
    public async Task EvaluateReadsRequestFromStdinAndSupportsComplexValues()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("Z = 3+4i\n");
        var request = JsonSerializer.Serialize(new { file = sandbox.WorksheetPath, isComplex = true, outputs = new[] { "Z" } });

        var result = await RunAsync(sandbox, "evaluate", "-", request);

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        var z = Value(json, "Z");
        Assert.True(z.GetProperty("isComplex").GetBoolean());
        Assert.Equal(3, z.GetProperty("real").GetDouble());
        Assert.Equal(4, z.GetProperty("imaginary").GetDouble());
    }

    [Fact]
    public async Task EvaluateResolvesRelativeIncludesFromWorksheetFolder()
    {
        using var sandbox = new Sandbox();
        Directory.CreateDirectory(sandbox.PathFor("shared"));
        await File.WriteAllTextAsync(sandbox.PathFor("shared/values.cpd"), "factor = 6\n");
        sandbox.WriteWorksheet("#include shared/values.cpd\ny = factor+2\n");
        var request = JsonSerializer.Serialize(new { file = sandbox.WorksheetPath, outputs = new[] { "y" } });

        var result = await RunAsync(sandbox, "evaluate", "-", request);

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Equal(8, Value(json, "y").GetProperty("real").GetDouble());
    }

    [Theory]
    [InlineData("0;1;3", "0;2;6", "2,2")]
    [InlineData("3;1;0", "6;2;0", "2,2")]
    [InlineData("0;1;1;3", "0;2;2;6", "2,2")]
    public async Task SweepReportsAdjacentSlopesInSuppliedOrder(string xs, string ys, string expectedSlopes)
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI x = 0\ny = 2*x\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            outputs = new[] { "y" },
            sweep = new { variable = "x", values = xs.Split(';') }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        var points = json.RootElement.GetProperty("points").EnumerateArray().ToArray();
        Assert.Equal(xs.Split(';').Length, points.Length);
        Assert.Equal(ys.Split(';').Select(double.Parse), points.Select(p => Value(p, "y").GetProperty("real").GetDouble()));
        var slopes = json.RootElement.GetProperty("sensitivities").EnumerateArray()
            .Select(s => s.GetProperty("deltaOutputPerDeltaInput").GetDouble());
        Assert.Equal(expectedSlopes.Split(',').Select(double.Parse), slopes);
    }

    [Fact]
    public async Task SweepKeepsPointFailuresAndDoesNotBridgeFailedPoints()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI x = 1\n#if x ≡ 2\ny = MissingValue\n#else\ny = 2*x\n#end if\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            outputs = new[] { "y" },
            sweep = new { variable = "x", values = new[] { "1", "2", "3" } }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(2, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        var points = json.RootElement.GetProperty("points").EnumerateArray().ToArray();
        Assert.Equal(3, points.Length);
        Assert.Empty(points[0].GetProperty("errors").EnumerateArray());
        Assert.NotEmpty(points[1].GetProperty("errors").EnumerateArray());
        Assert.Empty(points[2].GetProperty("errors").EnumerateArray());
        Assert.Empty(json.RootElement.GetProperty("sensitivities").EnumerateArray());
    }

    [Fact]
    public async Task SweepRequiresAppliedScalarInputAndScalarOutputs()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI x = 1\n#UI v = [1;2]\ny = 2*x\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            inputs = new { absent = "3" },
            outputs = new[] { "v", "unknown" },
            sweep = new { variable = "x", values = new[] { "1", "2" } }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(2, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Contains("absent", json.RootElement.GetProperty("errors").ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scalar", json.RootElement.GetProperty("errors").ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("points")[0].GetProperty("values").GetProperty("v").ValueKind);
    }

    [Fact]
    public async Task SweepUsesAppliedUiInputEvenWhenWorksheetReassignsThatNameLater()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI x = 1\ny = x^2\nx = 100\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            outputs = new[] { "y" },
            sweep = new { variable = "x", values = new[] { "2", "3" } }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        var points = json.RootElement.GetProperty("points").EnumerateArray().ToArray();
        Assert.Equal(new[] { 2d, 3d }, points.Select(p => p.GetProperty("x").GetProperty("real").GetDouble()));
        Assert.Equal(new[] { 4d, 9d }, points.Select(p => Value(p, "y").GetProperty("real").GetDouble()));
        Assert.Equal(5, json.RootElement.GetProperty("sensitivities")[0].GetProperty("deltaOutputPerDeltaInput").GetDouble());
    }

    [Fact]
    public async Task SweepOmitsSlopesForComplexOutputs()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI x = 1\ny = x+1i\n");
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
        Assert.Empty(json.RootElement.GetProperty("sensitivities").EnumerateArray());
    }

    [Fact]
    public async Task SweepOmitsSlopesWhenAValueIsNonfinite()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI x = 1\ny = 1/x\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            outputs = new[] { "y" },
            sweep = new { variable = "x", values = new[] { "1", "0" } }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Equal(JsonValueKind.String, Value(json.RootElement.GetProperty("points")[1], "y").GetProperty("real").ValueKind);
        Assert.Empty(json.RootElement.GetProperty("sensitivities").EnumerateArray());
    }

    [Fact]
    public async Task SweepOmitsSlopesWhenOutputUnitLabelsChange()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("#UI x = 1\n#if x ≡ 1\ny = 1m\n#else\ny = 1cm\n#end if\n");
        var request = new
        {
            file = sandbox.WorksheetPath,
            outputs = new[] { "y" },
            sweep = new { variable = "x", values = new[] { "1", "2" } }
        };

        var result = await RunAsync(sandbox, "sweep", "-", JsonSerializer.Serialize(request));

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        var points = json.RootElement.GetProperty("points").EnumerateArray().ToArray();
        Assert.Equal("m", Value(points[0], "y").GetProperty("unit").GetString());
        Assert.Equal("cm", Value(points[1], "y").GetProperty("unit").GetString());
        Assert.Empty(json.RootElement.GetProperty("sensitivities").EnumerateArray());
    }

    [Fact]
    public async Task OrdinaryAssignmentDoesNotMatchAnInputOverride()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet("x = 1\ny = 2*x\n");
        var request = JsonSerializer.Serialize(new { file = sandbox.WorksheetPath, inputs = new { x = "4" }, outputs = new[] { "y" } });

        var result = await RunAsync(sandbox, "evaluate", "-", request);

        Assert.Equal(2, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Contains("#UI", json.RootElement.GetProperty("errors").ToString());
    }

    [Theory]
    [InlineData("#pause")]
    [InlineData("#input")]
    public async Task EarlyInputOrPauseCannotReturnSuccessAfterRequestedOutputWasAssigned(string stopDirective)
    {
        using var sandbox = new Sandbox();
        sandbox.WriteWorksheet($"y = 4\n{stopDirective}\nz = 5\n");
        var request = JsonSerializer.Serialize(new { file = sandbox.WorksheetPath, outputs = new[] { "y" } });

        var result = await RunAsync(sandbox, "evaluate", "-", request);

        Assert.Equal(2, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Equal("calculation_error", json.RootElement.GetProperty("status").GetString());
        Assert.NotEmpty(json.RootElement.GetProperty("errors").EnumerateArray());
    }

    [Theory]
    [InlineData("{", 1)]
    [InlineData("null", 1)]
    [InlineData("{}", 1)]
    [InlineData("{\"file\":\"worksheet.cpd\",\"outputs\":null}", 1)]
    [InlineData("{\"file\":\"worksheet.cpd\",\"outputs\":[]}", 1)]
    [InlineData("{\"file\":\"worksheet.cpd\",\"outputs\":[null]}", 1)]
    [InlineData("{\"file\":\"worksheet.cpd\",\"outputs\":[\"y\"],\"inputs\":{\"x\":null}}", 1)]
    [InlineData("{\"file\":\"worksheet.cpd\",\"outputs\":[\"y\"],\"inputs\":{\"x\":\"\"}}", 1)]
    public async Task MalformedRequestsReturnJsonAndRequestError(string request, int expectedExitCode)
    {
        using var sandbox = new Sandbox();
        var result = await RunAsync(sandbox, "evaluate", "-", request);

        Assert.Equal(expectedExitCode, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Equal("error", json.RootElement.GetProperty("status").GetString());
        Assert.NotEmpty(json.RootElement.GetProperty("errors").EnumerateArray());
    }

    [Theory]
    [InlineData("{\"file\":\"missing.cpd\",\"outputs\":[\"y\"]}")]
    [InlineData("{\"file\":\"missing.cpd\",\"outputs\":[\"y\"],\"sweep\":null}")]
    [InlineData("{\"file\":\"missing.cpd\",\"outputs\":[\"y\"],\"sweep\":{\"variable\":\"x\",\"values\":[]}}")]
    [InlineData("{\"file\":\"missing.cpd\",\"outputs\":[\"y\"],\"sweep\":{\"variable\":\"x\",\"values\":null}}")]
    [InlineData("{\"file\":\"missing.cpd\",\"outputs\":[\"y\"],\"sweep\":{\"variable\":\"x\",\"values\":[null]}}")]
    [InlineData("{\"file\":\"missing.cpd\",\"outputs\":[\"y\"],\"sweep\":{\"variable\":\"\",\"values\":[\"1\"]}}")]
    public async Task InvalidSweepShapesFailBeforeWorksheetAccess(string request)
    {
        using var sandbox = new Sandbox();

        var result = await RunAsync(sandbox, "sweep", "-", request);

        Assert.Equal(1, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Equal("error", json.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("not found", json.RootElement.GetProperty("errors").ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingWorksheetReturnsRequestError()
    {
        using var sandbox = new Sandbox();
        var request = JsonSerializer.Serialize(new { file = Path.Combine(sandbox.Root, "missing.cpd"), outputs = new[] { "y" } });

        var result = await RunAsync(sandbox, "evaluate", "-", request);

        Assert.Equal(1, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Contains("not found", json.RootElement.GetProperty("errors").ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DataWriteDirectivesLeaveExistingAndNewFilesUntouched(bool append)
    {
        using var sandbox = new Sandbox();
        var existing = "existing.csv";
        var created = "must-not-exist.csv";
        await File.WriteAllTextAsync(sandbox.PathFor(existing), "sentinel");
        var keyword = append ? "#append" : "#write";
        sandbox.WriteWorksheet($"M = [1;2]\n{keyword} M to {existing}\n{keyword} M to {created}\ny = 4\n");
        var request = JsonSerializer.Serialize(new { file = sandbox.WorksheetPath, outputs = new[] { "y" } });

        var result = await RunAsync(sandbox, "evaluate", "-", request);

        Assert.Equal(0, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        Assert.Equal(4, Value(json, "y").GetProperty("real").GetDouble());
        Assert.Equal("sentinel", await File.ReadAllTextAsync(sandbox.PathFor(existing)));
        Assert.False(File.Exists(sandbox.PathFor(created)));
    }

    [Fact]
    public async Task Rejects1001SweepValuesBeforeOpeningWorksheet()
    {
        using var sandbox = new Sandbox();
        // Deliberately absent: an oversize sweep must be rejected before worksheet access.
        var request = JsonSerializer.Serialize(new
        {
            file = sandbox.PathFor("does not exist.cpd"),
            outputs = new[] { "y" },
            sweep = new { variable = "x", values = Enumerable.Repeat("1", 1001) }
        });

        var result = await RunAsync(sandbox, "sweep", "-", request);

        Assert.Equal(1, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        var errors = json.RootElement.GetProperty("errors").ToString();
        Assert.Contains("1,000", errors);
        Assert.DoesNotContain("not found", errors, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Exactly1000SweepValuesPassTheLimitCheckBeforeWorksheetAccess()
    {
        using var sandbox = new Sandbox();
        var request = JsonSerializer.Serialize(new
        {
            file = sandbox.PathFor("does not exist.cpd"),
            outputs = new[] { "y" },
            sweep = new { variable = "x", values = Enumerable.Repeat("1", 1000) }
        });

        var result = await RunAsync(sandbox, "sweep", "-", request);

        Assert.Equal(1, result.ExitCode);
        using var json = ParseSingleJsonDocument(result.Stdout);
        var errors = json.RootElement.GetProperty("errors").ToString();
        Assert.Contains("not found", errors, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1,000", errors);
    }

    private static JsonDocument ParseSingleJsonDocument(string stdout)
    {
        var trimmed = stdout.Trim();
        Assert.StartsWith("{", trimmed);
        Assert.EndsWith("}", trimmed);
        return JsonDocument.Parse(trimmed);
    }

    private static JsonElement Value(JsonDocument document, string name) => Value(document.RootElement, name);

    private static JsonElement Value(JsonElement point, string name) => point.GetProperty("values").GetProperty(name);

    private static async Task<ProcessResult> RunAsync(Sandbox sandbox, string command, string requestSource, string? stdin = null)
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(host) || !File.Exists(host))
            host = "dotnet";

        var cliAssembly = Path.Combine(AppContext.BaseDirectory, "cli", "Cli.dll");
        Assert.True(File.Exists(cliAssembly), $"CLI output was not copied to '{cliAssembly}'.");
        var start = new ProcessStartInfo(host)
        {
            WorkingDirectory = sandbox.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardInputEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(cliAssembly);
        start.ArgumentList.Add("agent");
        start.ArgumentList.Add(command);
        start.ArgumentList.Add("--request");
        start.ArgumentList.Add(requestSource);

        using var process = new Process { StartInfo = start };
        Assert.True(process.Start(), "Failed to start the CLI process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (stdin is not null)
            await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"CLI process exceeded {ProcessTimeout}.");
        }
        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

    private sealed class Sandbox : IDisposable
    {
        private readonly string _privateRoot = Path.Combine(Path.GetTempPath(), "calcpad cli tests", Guid.NewGuid().ToString("N"));
        public string Root { get; private set; }
        public string WorksheetPath => PathFor("worksheet.cpd");

        public Sandbox(string? worksheetDirectory = null)
        {
            Root = worksheetDirectory is null ? _privateRoot : Path.Combine(_privateRoot, worksheetDirectory);
            Directory.CreateDirectory(Root);
        }

        public string PathFor(string name) => Path.Combine(Root, name);

        public void WriteWorksheet(string source) => File.WriteAllText(WorksheetPath, source);

        public string WriteRequest(object request)
        {
            var requestPath = PathFor("request.json");
            File.WriteAllText(requestPath, JsonSerializer.Serialize(request));
            return requestPath;
        }

        public void Dispose()
        {
            try { Directory.Delete(_privateRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
