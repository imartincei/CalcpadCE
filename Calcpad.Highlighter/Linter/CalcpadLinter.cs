using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Helpers;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Linter.Validators.Stage1;
using Calcpad.Highlighter.Linter.Validators.Stage2;
using Calcpad.Highlighter.Linter.Validators.Stage3;

namespace Calcpad.Highlighter.Linter
{
    public class CalcpadLinter
    {
        private readonly IncludeValidator _includeValidator = new();
        private readonly MacroValidator _macroValidator = new();
        private readonly NamingValidator _namingValidator = new();
        private readonly UsageValidator _usageValidator = new();
        private readonly FunctionTypeValidator _functionTypeValidator = new();
        private readonly CommandBlockValidator _commandBlockValidator = new();
        private readonly HtmlCommentValidator _htmlCommentValidator = new();
        private readonly SettingsValidator _settingsValidator = new();
        private readonly UiValidator _uiValidator = new();
        private readonly LegacyInputValidator _legacyInputValidator = new();
        private readonly DeprecatedInputKeywordValidator _deprecatedInputKeywordValidator = new();
        private readonly CoreCheckValidator _coreCheckValidator = new();
        private readonly RunErrorValidator _runErrorValidator = new();

        // Static checks that report, more precisely, what Core gives these codes on the same line
        private static readonly Dictionary<string, string[]> CoveredCoreCodes = new()
        {
            ["CPD-3307"] = ["CPD-3302", "CPD-5110", "CPD-3401"],
            ["CPD-3308"] = ["CPD-3302", "CPD-5110", "CPD-3401"],
            ["CPD-3311"] = ["CPD-3302", "CPD-5110", "CPD-3401"],
            ["CPD-3405"] = ["CPD-3401"],
        };

        /// <summary>
        /// Lint code using pre-processed staged content from ContentResolver.
        /// </summary>
        /// <param name="staged">Staged resolved content from ContentResolver.</param>
        /// <param name="ignoreRegions">
        /// Optional source-level regions in which specific diagnostic codes are suppressed, with
        /// 0-based original source line numbers. Applied after all diagnostics are mapped back.
        /// </param>
        /// <param name="runErrors">The last run's errors, numbered by this content's lines (see <see cref="RunErrors.For"/>).</param>
        public LinterResult Lint(StagedResolvedContent staged,
            IReadOnlyList<LintIgnoreRegion> ignoreRegions = null,
            IReadOnlyList<Calcpad.Core.CalcpadError> runErrors = null)
        {
            if (staged == null)
            {
                return new LinterResult();
            }

            var result = new LinterResult();

            // Convert ContentResolver results to linter contexts
            var stage1Context = ConvertToStage1Context(staged.Stage1);
            var stage2Context = ConvertToStage2Context(staged.Stage2, staged.Stage1);
            var stage3Context = ConvertToStage3Context(staged.Stage3, staged.Stage2);
            stage3Context.Runtime = staged.Runtime;

            // Set stage contexts on result for automatic line continuation mapping
            result.SetStageContexts(stage1Context, stage2Context, stage3Context);

            // Create tokenizer for stage 3 content
            var tokenProvider = new TokenizedLineProvider();

            tokenProvider.SetMacroParameters(staged.Stage2.MacroParameterOrder);

            tokenProvider.Tokenize(stage3Context.Lines);

            // Run validators
            ValidateStage1(stage1Context, result);
            ValidateStage2(stage2Context, result);
            ValidateStage3(stage3Context, result, tokenProvider);

            // A failed MacroParser leaves the text partly expanded, so only its own errors count
            if (staged.Stage2.Errors.Count == 0)
            {
                if (staged.CoreCheck is { } check)
                    _coreCheckValidator.Validate(stage3Context, check.Value, result);
                if (runErrors != null)
                    _runErrorValidator.Validate(staged, stage3Context, runErrors, result);
            }

            // Map all diagnostics from stage lines to original lines
            result.MapDiagnosticsToOriginal();
            RemoveDuplicates(result);

            // Suppress diagnostics covered by LintIgnore regions
            if (ignoreRegions is { Count: > 0 })
                ApplyIgnoreRegions(result, ignoreRegions);

            return result;
        }

        public Task<LinterResult> LintAsync(StagedResolvedContent staged,
            IReadOnlyList<LintIgnoreRegion> ignoreRegions = null,
            IReadOnlyList<Calcpad.Core.CalcpadError> runErrors = null)
        {
            return Task.Run(() => Lint(staged, ignoreRegions, runErrors));
        }

        /// <summary>
        /// One diagnostic per line and code. The static checks that remain are the more precise,
        /// and the check pass already reports what a run of the same text would.
        /// </summary>
        private static void RemoveDuplicates(LinterResult result)
        {
            var seen = new HashSet<(int, string)>();
            var duplicates = new HashSet<LinterDiagnostic>();
            foreach (var d in result.Diagnostics.OrderBy(d => d.Origin))
            {
                if (d.Origin == DiagnosticOrigin.Static)
                {
                    seen.Add((d.Line, d.Code));
                    foreach (var code in CoveredCoreCodes.GetValueOrDefault(d.Code, []))
                        seen.Add((d.Line, code));
                }
                else if (!seen.Add((d.Line, d.Code)))
                    duplicates.Add(d);
            }
            if (duplicates.Count > 0)
                result.RemoveDiagnostics(duplicates.Contains);
        }

        private static void ApplyIgnoreRegions(
            LinterResult result,
            IReadOnlyList<LintIgnoreRegion> regions)
        {
            result.RemoveDiagnostics(d =>
                regions.Any(r =>
                    d.Line >= r.StartLine &&
                    d.Line <= r.EndLine &&
                    (r.Codes.Count == 0 ||
                     r.Codes.Contains(d.Code, StringComparer.OrdinalIgnoreCase))));
        }

        private Stage1Context ConvertToStage1Context(Stage1Result stage1Result)
        {
            var context = new Stage1Context();
            context.Lines.AddRange(stage1Result.Lines);
            foreach (var kvp in stage1Result.SourceMap)
            {
                context.SourceMap[kvp.Key] = kvp.Value;
            }

            // Copy line continuation data
            if (stage1Result.LineContinuationMap != null)
            {
                foreach (var kvp in stage1Result.LineContinuationMap)
                {
                    context.LineContinuationMap[kvp.Key] = kvp.Value;
                }
            }

            if (stage1Result.LineContinuationSegments != null)
            {
                foreach (var kvp in stage1Result.LineContinuationSegments)
                {
                    context.LineContinuationSegments[kvp.Key] = kvp.Value;
                }
            }

            return context;
        }

        private Stage2Context ConvertToStage2Context(Stage2Result stage2Result, Stage1Result stage1Result)
        {
            var context = new Stage2Context();
            context.Lines.AddRange(stage2Result.Lines);

            foreach (var kvp in stage2Result.SourceMap)
            {
                context.SourceMap[kvp.Key] = stage1Result.SourceMap.GetValueOrDefault(kvp.Value, kvp.Value);
                context.Stage2ToStage1Map[kvp.Key] = kvp.Value;
            }

            // Convert macro definitions
            foreach (var macro in stage2Result.MacroDefinitions)
            {
                context.MacroDefinitions.Add(new Models.MacroDefinition
                {
                    Name = macro.Name,
                    Parameters = macro.Params,
                    LineNumber = macro.LineNumber
                });
            }
            context.Errors = stage2Result.Errors;

            return context;
        }

        private Stage3Context ConvertToStage3Context(Stage3Result stage3Result, Stage2Result stage2Result)
        {
            var context = new Stage3Context();
            context.Lines.AddRange(stage3Result.Lines);

            foreach (var kvp in stage3Result.SourceMap)
            {
                var stage2Line = kvp.Value;
                var stage1Line = stage2Result.SourceMap.GetValueOrDefault(stage2Line, stage2Line);
                context.SourceMap[kvp.Key] = stage1Line;
                context.Stage3ToStage2Map[kvp.Key] = stage2Line;
            }

            if (stage3Result.IncludeMap != null)
            {
                foreach (var kvp in stage3Result.IncludeMap)
                {
                    context.IncludeMap[kvp.Key] = kvp.Value;
                }
            }

            context.DefinedVariables = stage3Result.DefinedVariables;
            context.VariableFirstStage3Lines = stage3Result.VariableFirstStage3Lines;
            context.VariableFirstLines = stage3Result.VariableFirstLines;
            context.DefinedFunctions = stage3Result.UserDefinedFunctions;

            // Populate macro info dictionary
            foreach (var kvp in stage3Result.UserDefinedMacros)
            {
                context.DefinedMacros[kvp.Key] = kvp.Value;
            }

            if (stage3Result.MacroExpansions != null)
            {
                foreach (var kvp in stage3Result.MacroExpansions)
                {
                    context.MacroExpansions[kvp.Key] = kvp.Value;
                }
            }

            // Convert custom units to HashSet of names
            context.CustomUnits = stage3Result.CustomUnits.Select(u => u.Name).ToHashSet();

            // Copy TypeTracker
            context.TypeTracker = stage3Result.TypeTracker;

            // Copy command block functions
            context.CommandBlockFunctions = stage3Result.CommandBlockFunctions;

            // Copy reassignment tracking
            context.VariableReassignments = stage3Result.VariableReassignments;
            context.OuterScopeAssignments = stage3Result.OuterScopeAssignments;

            // Copy variable assignment/usage data for unused variable detection
            context.VariableAssignments = stage3Result.VariableAssignments;
            context.VariableUsages = stage3Result.VariableUsages;

            return context;
        }

        private void ValidateStage1(Stage1Context context, LinterResult result)
        {
            _includeValidator.Validate(context, result);
        }

        private void ValidateStage2(Stage2Context stage2, LinterResult result)
        {
            _macroValidator.Validate(stage2, result);
        }

        private void ValidateStage3(Stage3Context stage3, LinterResult result, TokenizedLineProvider tokenProvider)
        {
            _namingValidator.Validate(stage3, result, tokenProvider);
            _usageValidator.Validate(stage3, result, tokenProvider);
            _functionTypeValidator.Validate(stage3, result, tokenProvider);
            _commandBlockValidator.Validate(stage3, result, tokenProvider);
            _htmlCommentValidator.Validate(stage3, result, tokenProvider);
            _settingsValidator.Validate(stage3, result, tokenProvider);
            _uiValidator.Validate(stage3, result, tokenProvider);
            _legacyInputValidator.Validate(stage3, result, tokenProvider);
            _deprecatedInputKeywordValidator.Validate(stage3, result, tokenProvider);
        }
    }
}
