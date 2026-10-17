#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calcpad.Core;

namespace Calcpad.Cli;

internal static class AgentCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length != 4 || args[1] is not ("evaluate" or "sweep") || args[2] != "--request")
                throw new ArgumentException("Usage: calcpad agent evaluate|sweep --request <file|->");

            var json = ReadRequest(args[3]);
            var request = JsonSerializer.Deserialize<AgentRequest>(json, JsonOptions)
                ?? throw new ArgumentException("The request JSON is empty.");
            var response = args[1] == "sweep" ? RunSweep(request) : RunEvaluate(request);
            Console.Out.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
            return response.Errors.Count == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Out.WriteLine(JsonSerializer.Serialize(new AgentResponse
            {
                Status = "error",
                Errors = [ex.Message]
            }, JsonOptions));
            return 1;
        }
    }

    private static string ReadRequest(string path)
    {
        if (path != "-")
            return File.ReadAllText(path);

        using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static AgentResponse RunEvaluate(AgentRequest request)
    {
        Validate(request);
        var worksheet = Prepare(request);
        var run = Evaluate(worksheet, request.Inputs ?? []);
        var errors = AddMissingOutputs(run.Errors, run.Results, request.Outputs);
        return new AgentResponse
        {
            Status = errors.Count == 0 ? "ok" : "calculation_error",
            Values = SelectOutputs(run.Results, request.Outputs),
            Errors = errors
        };
    }

    private static AgentResponse RunSweep(AgentRequest request)
    {
        Validate(request);
        if (request.Sweep is null || string.IsNullOrWhiteSpace(request.Sweep.Variable) || request.Sweep.Values is not { Count: > 0 })
            throw new ArgumentException("A sweep requires a variable and at least one value.");
        // Keep this check ahead of Prepare: an oversized request must not open or parse the worksheet.
        if (request.Sweep.Values.Count > 1000)
            throw new ArgumentException("A sweep may contain at most 1,000 values.");
        if (request.Sweep.Values.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Sweep values must be non-empty strings.");

        var worksheet = Prepare(request);
        var points = new List<AgentSweepPoint>();
        foreach (var value in request.Sweep.Values)
        {
            var inputs = new Dictionary<string, string>(request.Inputs ?? [], StringComparer.Ordinal)
            {
                [request.Sweep.Variable] = value
            };
            var run = Evaluate(worksheet, inputs);
            run.Inputs.TryGetValue(request.Sweep.Variable, out var inputValue);
            var pointErrors = AddMissingOutputs(run.Errors, run.Results, request.Outputs);
            if (inputValue is null)
                pointErrors.Add($"Sweep input '{request.Sweep.Variable}' did not produce a scalar value.");
            points.Add(new AgentSweepPoint
            {
                Input = value,
                X = inputValue,
                Values = SelectOutputs(run.Results, request.Outputs),
                Errors = pointErrors
            });
        }

        var sensitivities = new List<AgentSensitivity>();
        for (var i = 1; i < points.Count; ++i)
        {
            if (points[i - 1].Errors.Count > 0 || points[i].Errors.Count > 0 ||
                points[i - 1].X is not { IsComplex: false } x0 || points[i].X is not { IsComplex: false } x1 ||
                !double.IsFinite(x0.Real) || !double.IsFinite(x1.Real) || x1.Real == x0.Real ||
                !string.Equals(x0.Unit, x1.Unit, StringComparison.Ordinal))
                continue;
            foreach (var output in request.Outputs.Distinct(StringComparer.Ordinal))
            {
                if (points[i - 1].Values.TryGetValue(output, out var y0) &&
                    points[i].Values.TryGetValue(output, out var y1) &&
                    y0 is { IsComplex: false } && y1 is { IsComplex: false } &&
                    double.IsFinite(y0.Real) && double.IsFinite(y1.Real) &&
                    string.Equals(y0.Unit, y1.Unit, StringComparison.Ordinal))
                {
                    var deltaInput = x1.Real - x0.Real;
                    var deltaOutput = y1.Real - y0.Real;
                    if (!double.IsFinite(deltaInput) || !double.IsFinite(deltaOutput))
                        continue;

                    var slope = deltaOutput / deltaInput;
                    if (double.IsFinite(slope))
                        sensitivities.Add(new AgentSensitivity(output, i - 1, i, slope, x0.Unit, y0.Unit));
                }
            }
        }

        var errors = points.SelectMany(p => p.Errors).Distinct().ToList();
        return new AgentResponse
        {
            Status = errors.Count == 0 ? "ok" : "calculation_error",
            SweepVariable = request.Sweep.Variable,
            Points = points,
            Sensitivities = sensitivities,
            Errors = errors
        };
    }

    private static PreparedWorksheet Prepare(AgentRequest request)
    {
        var file = Path.GetFullPath(request.File);
        if (!File.Exists(file))
            throw new FileNotFoundException("Worksheet not found.", file);

        var macros = new MacroParser
        {
            SourceFilePath = file,
            Include = static (path, _) => File.ReadAllText(path)
        };
        var hasMacroErrors = macros.Parse(File.ReadAllText(file), out var source, null, 0, true);
        var errors = macros.Errors.Select(e => e.SourceLine > 0 ? $"line {e.SourceLine}: {e.Message}" : e.Message).ToList();
        return new PreparedWorksheet(file, source, macros.PathRoots, request.IsComplex, hasMacroErrors, errors);
    }

    private static (Dictionary<string, ScalarResult> Results, Dictionary<string, ScalarResult> Inputs, List<string> Errors) Evaluate(PreparedWorksheet worksheet, Dictionary<string, string> inputs)
    {
        var errors = new List<string>(worksheet.Errors);
        if (worksheet.HasMacroErrors)
            return ([], [], errors);

        var parser = new ExpressionParser
        {
            SourceFilePath = worksheet.File,
            PathRoots = worksheet.PathRoots,
            Settings = CreateSettings(worksheet.IsComplex),
            AllowDataWrite = false,
            EnableUi = false,
            Debug = true,
            CaptureScalarResults = true,
            UiOverrides = inputs
        };
        parser.Parse(worksheet.Source, true, false);
        errors.AddRange(parser.Errors.Select(e => $"line {e.SourceLine}: {e.Message}"));
        if (parser.IsPaused)
            errors.Add("Worksheet calculation paused before completion.");

        foreach (var input in inputs.Keys)
        {
            if (!parser.AppliedUiOverrides.Contains(input))
                errors.Add($"Input '{input}' did not match a scalar #UI assignment.");
            else if (!parser.UiInputResults.ContainsKey(input))
                errors.Add($"Input '{input}' did not produce a scalar value.");
        }

        return (parser.ScalarResults.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            parser.UiInputResults.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal), errors);
    }

    private static Settings CreateSettings(bool isComplex)
    {
        var settings = new Settings();
        settings.Math.IsComplex = isComplex;
        return settings;
    }

    private static Dictionary<string, ScalarResult?> SelectOutputs(Dictionary<string, ScalarResult> results, List<string> outputs) =>
        outputs.Distinct(StringComparer.Ordinal).ToDictionary(name => name,
            name => results.TryGetValue(name, out var result) ? result : null, StringComparer.Ordinal);

    private static List<string> AddMissingOutputs(List<string> errors, Dictionary<string, ScalarResult> results, List<string> outputs)
    {
        var all = new List<string>(errors);
        foreach (var output in outputs.Distinct(StringComparer.Ordinal))
            if (!results.ContainsKey(output))
                all.Add($"Output '{output}' was not assigned a scalar value.");
        return all;
    }

    private static void Validate(AgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.File))
            throw new ArgumentException("Request field 'file' is required.");
        if (request.Outputs is not { Count: > 0 } outputs || outputs.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Request field 'outputs' must contain at least one variable name.");
        if (request.Inputs?.Any(kv => string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value)) == true)
            throw new ArgumentException("Input names and values must be non-empty strings.");
    }

    private sealed record PreparedWorksheet(string File, string Source, PathRoots PathRoots, bool IsComplex, bool HasMacroErrors, List<string> Errors);

}

internal sealed class AgentRequest
{
    public string File { get; set; } = "";
    public bool IsComplex { get; set; }
    public Dictionary<string, string>? Inputs { get; set; }
    public List<string> Outputs { get; set; } = [];
    public AgentSweepRequest? Sweep { get; set; }
}

internal sealed class AgentSweepRequest
{
    public string Variable { get; set; } = "";
    public List<string> Values { get; set; } = [];
}

internal sealed class AgentResponse
{
    public string SchemaVersion { get; set; } = "1";
    public string Status { get; set; } = "ok";
    public Dictionary<string, ScalarResult?>? Values { get; set; }
    public string? SweepVariable { get; set; }
    public List<AgentSweepPoint>? Points { get; set; }
    public List<AgentSensitivity>? Sensitivities { get; set; }
    public List<string> Errors { get; set; } = [];
}

internal sealed class AgentSweepPoint
{
    public string Input { get; set; } = "";
    public ScalarResult? X { get; set; }
    public Dictionary<string, ScalarResult?> Values { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

internal sealed record AgentSensitivity(string Output, int FromPoint, int ToPoint, double DeltaOutputPerDeltaInput, string? InputUnit, string? OutputUnit);
