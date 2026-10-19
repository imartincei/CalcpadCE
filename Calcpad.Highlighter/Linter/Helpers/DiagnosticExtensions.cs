using Calcpad.Highlighter.Linter.Constants;
using Calcpad.Highlighter.Linter.Models;

namespace Calcpad.Highlighter.Linter.Helpers
{
    /// <summary>
    /// Specifies which processing stage a line number refers to.
    /// </summary>
    public enum LineStage
    {
        /// <summary>Stage 1: After line continuation processing</summary>
        Stage1,
        /// <summary>Stage 2: After include/macro expansion</summary>
        Stage2,
        /// <summary>Stage 3: After full processing (default)</summary>
        Stage3
    }

    public static class DiagnosticExtensions
    {
        /// <summary>
        /// Adds a diagnostic with custom message details (Stage 3 line).
        /// Line mapping to original is deferred until MapDiagnosticsToOriginal is called.
        /// </summary>
        public static void AddDiagnostic(
            this LinterResult result,
            int line,
            int column,
            int endColumn,
            string code,
            string details,
            LinterSeverity severity)
        {
            AddDiagnostic(result, line, column, endColumn, code, details, severity, LineStage.Stage3);
        }

        /// <summary>
        /// Adds a diagnostic with custom message details from a specific stage.
        /// Line mapping to original is deferred until MapDiagnosticsToOriginal is called.
        /// </summary>
        public static void AddDiagnostic(
            this LinterResult result,
            int line,
            int column,
            int endColumn,
            string code,
            string details,
            LinterSeverity severity,
            LineStage stage)
        {
            var message = ErrorCodes.GetDescription(code) + ": " + details;
            var diagnostic = new LinterDiagnostic
            {
                StageLine = line,
                Stage = stage,
                Line = line, // Will be mapped later
                Column = column,
                EndColumn = endColumn,
                Code = code,
                Message = message,
                Severity = severity
            };
            result.AppendDiagnostic(diagnostic);
        }

        /// <summary>
        /// Adds a diagnostic using only the error code's description (Stage 3 line).
        /// Line mapping to original is deferred until MapDiagnosticsToOriginal is called.
        /// </summary>
        public static void AddDiagnostic(
            this LinterResult result,
            int line,
            int column,
            int endColumn,
            string code,
            LinterSeverity severity)
        {
            var message = ErrorCodes.GetDescription(code);
            var diagnostic = new LinterDiagnostic
            {
                StageLine = line,
                Stage = LineStage.Stage3,
                Line = line, // Will be mapped later
                Column = column,
                EndColumn = endColumn,
                Code = code,
                Message = message,
                Severity = severity
            };
            result.AppendDiagnostic(diagnostic);
        }

        /// <summary>
        /// Adds an error diagnostic with custom message details (Stage 3 line).
        /// </summary>
        public static void AddError(
            this LinterResult result,
            int line,
            int column,
            int endColumn,
            string code,
            string details)
        {
            result.AddDiagnostic(line, column, endColumn, code, details, LinterSeverity.Error, LineStage.Stage3);
        }

        /// <summary>
        /// Adds an error diagnostic from a specific stage.
        /// </summary>
        public static void AddError(
            this LinterResult result,
            int line,
            int column,
            int endColumn,
            string code,
            string details,
            LineStage stage)
        {
            result.AddDiagnostic(line, column, endColumn, code, details, LinterSeverity.Error, stage);
        }

        /// <summary>
        /// Adds an error diagnostic using only the error code's description (Stage 3 line).
        /// </summary>
        public static void AddError(
            this LinterResult result,
            int line,
            int column,
            int endColumn,
            string code)
        {
            result.AddDiagnostic(line, column, endColumn, code, LinterSeverity.Error);
        }

        /// <summary>
        /// Adds a warning diagnostic with custom message details (Stage 3 line).
        /// </summary>
        public static void AddWarning(
            this LinterResult result,
            int line,
            int column,
            int endColumn,
            string code,
            string details)
        {
            result.AddDiagnostic(line, column, endColumn, code, details, LinterSeverity.Warning, LineStage.Stage3);
        }

        /// <summary>
        /// Adds a warning diagnostic from a specific stage.
        /// </summary>
        public static void AddWarning(
            this LinterResult result,
            int line,
            int column,
            int endColumn,
            string code,
            string details,
            LineStage stage)
        {
            result.AddDiagnostic(line, column, endColumn, code, details, LinterSeverity.Warning, stage);
        }

        /// <summary>
        /// Adds a warning diagnostic using only the error code's description (Stage 3 line).
        /// </summary>
        public static void AddWarning(
            this LinterResult result,
            int line,
            int column,
            int endColumn,
            string code)
        {
            result.AddDiagnostic(line, column, endColumn, code, LinterSeverity.Warning);
        }

        /// <summary>
        /// Adds an information diagnostic with custom message details (Stage 3 line).
        /// </summary>
        public static void AddInformation(
            this LinterResult result,
            int line,
            int column,
            int endColumn,
            string code,
            string details)
        {
            result.AddDiagnostic(line, column, endColumn, code, details, LinterSeverity.Information, LineStage.Stage3);
        }

        /// <summary>
        /// Adds an information diagnostic from a specific stage.
        /// </summary>
        public static void AddInformation(
            this LinterResult result,
            int line,
            int column,
            int endColumn,
            string code,
            string details,
            LineStage stage)
        {
            result.AddDiagnostic(line, column, endColumn, code, details, LinterSeverity.Information, stage);
        }

        /// <summary>
        /// Adds an information diagnostic using only the error code's description (Stage 3 line).
        /// </summary>
        public static void AddInformation(
            this LinterResult result,
            int line,
            int column,
            int endColumn,
            string code)
        {
            result.AddDiagnostic(line, column, endColumn, code, LinterSeverity.Information);
        }
    }
}
