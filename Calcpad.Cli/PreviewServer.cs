using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Calcpad.Core;

namespace Calcpad.Cli
{
    // Long-running headless render server for the VS Code preview extension.
    // Protocol: newline-delimited JSON (NDJSON) — one PreviewRequest per stdin line,
    // one PreviewResponse per stdout line. Keeps a warm process so live preview is fast.
    internal static class PreviewServer
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        internal static void Run()
        {
            // The render pipeline and any reused helper may write to the console; redirect
            // Console.Out to nowhere so it can never pollute the NDJSON protocol stream.
            Console.SetOut(TextWriter.Null);

            // Always produce '.' as the decimal separator, regardless of machine locale.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            using var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);

            var settings = LoadSettings();

            string line;
            while ((line = stdin.ReadLine()) is not null)
            {
                if (line.Length == 0)
                    continue;

                PreviewResponse response;
                long id = 0;
                try
                {
                    var request = JsonSerializer.Deserialize<PreviewRequest>(line, JsonOptions);
                    id = request?.Id ?? 0;
                    if (request?.Export is not null)
                    {
                        var outPath = Export(request, settings);
                        response = new PreviewResponse { Id = id, Ok = true, OutPath = outPath };
                    }
                    else
                    {
                        var html = Render(request, settings);
                        response = new PreviewResponse { Id = id, Ok = true, Html = html };
                    }
                }
                catch (Exception ex)
                {
                    response = new PreviewResponse { Id = id, Ok = false, Error = ex.Message };
                }
                stdout.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
            }
        }

        private static Settings LoadSettings()
        {
            try
            {
                return Program.GetSettings();
            }
            catch
            {
                // GetSettings may try to prompt on a corrupt settings file; never block here.
                var s = new Settings();
                s.Math.Decimals = 6;
                return s;
            }
        }

        private static string Render(PreviewRequest req, Settings settings)
        {
            if (req is null)
                return Body(string.Empty);

            var unwrappedCode = Prepare(req, settings, out var macroErrorHtml);
            if (macroErrorHtml is not null)
                return Body(macroErrorHtml);

            var parser = new ExpressionParser { Settings = settings };
            parser.Parse(unwrappedCode, true, false);
            return Body(parser.HtmlResult);
        }

        // Renders the worksheet (with the current input values) to a file via the same
        // Converter the one-shot CLI uses, so the export matches the calculated output.
        private static string Export(PreviewRequest req, Settings settings)
        {
            var format = (req.Export.Format ?? "html").Trim().ToLowerInvariant();
            var outPath = req.Export.OutPath;
            if (string.IsNullOrWhiteSpace(outPath))
                throw new ArgumentException("Missing export output path.");

            var unwrappedCode = Prepare(req, settings, out var macroErrorHtml);
            if (macroErrorHtml is not null)
                throw new InvalidOperationException("Cannot export: the worksheet has macro or #include errors.");

            var getXml = format is "docx";
            var parser = new ExpressionParser { Settings = settings };
            parser.Parse(unwrappedCode, true, getXml);

            var converter = new Converter(isSilent: true, isBodyOnly: false);
            switch (format)
            {
                case "html":
                case "htm":
                    converter.ToHtml(parser.HtmlResult, outPath);
                    break;
                case "docx":
                    converter.ToOpenXml(parser.HtmlResult, outPath, parser.OpenXmlExpressions);
                    break;
                default:
                    throw new ArgumentException($"Unsupported export format: {format}");
            }
            return outPath;
        }

        // Shared preparation: resolve paths, set units, read & macro-expand the source,
        // and inject interactive input values. Returns the unwrapped code, or null with
        // macroErrorHtml set when macro/include expansion failed.
        private static string Prepare(PreviewRequest req, Settings settings, out string macroErrorHtml)
        {
            macroErrorHtml = null;

            // Resolve relative #include / image paths against the document folder when known.
            // Resolve to an absolute path BEFORE changing the working directory, otherwise a
            // relative SourcePath would be resolved against the previous request's folder.
            string fullPath = null;
            if (!string.IsNullOrEmpty(req.SourcePath))
            {
                fullPath = Path.GetFullPath(req.SourcePath);
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    Directory.SetCurrentDirectory(dir);
            }

            // Reset units each render so settings don't leak across documents (warm process).
            settings.Units = string.IsNullOrEmpty(req.Units) ? "m" : req.Units;

            var code = req.SourceText is not null
                ? CalcpadReader.ReadText(req.SourceText)
                : CalcpadReader.Read(fullPath);

            var macroParser = new MacroParser { Include = CalcpadReader.Include };
            var hasMacroErrors = macroParser.Parse(code, out var unwrappedCode, null, 0, true);
            if (hasMacroErrors)
            {
                macroErrorHtml = CalcpadReader.CodeToHtml(unwrappedCode);
                return null;
            }

            if (req.InputValues is { Count: > 0 })
                unwrappedCode = ApplyInputValues(unwrappedCode, req.InputValues);

            return unwrappedCode;
        }

        // Injects the interactive input-field values back into the source as {value} tokens,
        // mirroring Calcpad.Wpf MainWindow.SetInputFields, then lets the parser recalc.
        private static string ApplyInputValues(string source, List<InputValue> inputs)
        {
            var byLine = new Dictionary<int, Queue<string>>();
            foreach (var iv in inputs)
            {
                if (iv is null || iv.Line <= 0)
                    continue;

                if (!byLine.TryGetValue(iv.Line, out var queue))
                {
                    queue = new Queue<string>();
                    byLine[iv.Line] = queue;
                }
                queue.Enqueue((iv.Value ?? string.Empty).Trim());
            }
            if (byLine.Count == 0)
                return source;

            var lines = source.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            for (var i = 0; i < lines.Length; ++i)
            {
                if (byLine.TryGetValue(i + 1, out var queue) && queue.Count != 0)
                {
                    sb.Clear();
                    if (MacroParser.SetLineInputFields(lines[i].TrimEnd(), sb, queue, true))
                        lines[i] = sb.ToString();
                }
            }
            return string.Join('\n', lines);
        }

        private static string Body(string html) => $"<div class=\"calcpad-output\">{html}</div>";

        private sealed class PreviewRequest
        {
            public long Id { get; set; }
            public string SourcePath { get; set; }
            public string SourceText { get; set; }
            public string Units { get; set; }
            public List<InputValue> InputValues { get; set; }
            public ExportRequest Export { get; set; }
        }

        private sealed class InputValue
        {
            public int Line { get; set; }
            public string Value { get; set; }
        }

        private sealed class ExportRequest
        {
            public string Format { get; set; }
            public string OutPath { get; set; }
        }

        private sealed class PreviewResponse
        {
            public long Id { get; set; }
            public bool Ok { get; set; }
            public string Html { get; set; }
            public string OutPath { get; set; }
            public string Error { get; set; }
        }
    }
}
