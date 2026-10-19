using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for $ commands (iterative methods, plotting, etc.).
    /// </summary>
    public static class CommandSnippets
    {
        public static readonly SnippetItem[] Items =
        [
            // ============================================
            // ITERATIVE AND NUMERICAL METHODS
            // ============================================
            new SnippetItem
            {
                Insert = "$Root{§ @ § = § : §}",
                Description = "Root finding for f(x) = 0",
                Label = "$Root{f(x) @ x = a : b}",
                Category = "Numerical Methods",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "f(x)", Type = ParameterType.Expression, Description = "Function to find root of" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Variable" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Scalar, Description = "Start of interval" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Scalar, Description = "End of interval" }
                ]
            },
            new SnippetItem
            {
                Insert = "$Root{§ = § @ § = § : §}",
                Description = "Root finding for f(x) = const",
                Label = "$Root{f(x) = c @ x = a : b}",
                Category = "Numerical Methods",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "f(x)", Type = ParameterType.Expression, Description = "Function" },
                    new SnippetParameter { Name = "const", Type = ParameterType.Scalar, Description = "Target value" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Variable" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Scalar, Description = "Start of interval" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Scalar, Description = "End of interval" }
                ]
            },
            new SnippetItem
            {
                Insert = "$Find{§ @ § = § : §}",
                Description = "Find approximate solution (x not required to be precise)",
                Label = "$Find{f(x) @ x = a : b}",
                Category = "Numerical Methods",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "f(x)", Type = ParameterType.Expression, Description = "Function" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Variable" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Scalar, Description = "Start of interval" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Scalar, Description = "End of interval" }
                ]
            },
            new SnippetItem
            {
                Insert = "$Sup{§ @ § = § : §}",
                Description = "Find local maximum of a function",
                Label = "$Sup{f(x) @ x = a : b}",
                Category = "Numerical Methods",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "f(x)", Type = ParameterType.Expression, Description = "Function to maximize" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Variable" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Scalar, Description = "Start of interval" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Scalar, Description = "End of interval" }
                ]
            },
            new SnippetItem
            {
                Insert = "$Inf{§ @ § = § : §}",
                Description = "Find local minimum of a function",
                Label = "$Inf{f(x) @ x = a : b}",
                Category = "Numerical Methods",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "f(x)", Type = ParameterType.Expression, Description = "Function to minimize" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Variable" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Scalar, Description = "Start of interval" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Scalar, Description = "End of interval" }
                ]
            },
            new SnippetItem
            {
                Insert = "$Area{§ @ § = § : §}",
                Description = "Numerical integration (adaptive Gauss-Lobatto)",
                Label = "$Area{f(x) @ x = a : b}",
                Category = "Numerical Methods",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "f(x)", Type = ParameterType.Expression, Description = "Function to integrate" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Variable" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Scalar, Description = "Lower limit" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Scalar, Description = "Upper limit" }
                ]
            },
            new SnippetItem
            {
                Insert = "$Integral{§ @ § = § : §}",
                Description = "Numerical integration (Tanh-Sinh)",
                Label = "$Integral{f(x) @ x = a : b}",
                Category = "Numerical Methods",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "f(x)", Type = ParameterType.Expression, Description = "Function to integrate" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Variable" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Scalar, Description = "Lower limit" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Scalar, Description = "Upper limit" }
                ]
            },
            new SnippetItem
            {
                Insert = "$Slope{§ @ § = §}",
                Description = "Numerical differentiation (slope at point)",
                Label = "$Slope{f(x) @ x = a}",
                Category = "Numerical Methods",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "f(x)", Type = ParameterType.Expression, Description = "Function to differentiate" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Variable" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Scalar, Description = "Point of evaluation" }
                ]
            },

            // ============================================
            // ITERATIVE OPERATIONS
            // ============================================
            new SnippetItem
            {
                Insert = "$Sum{§ @ § = § : §}",
                Description = "Iterative sum",
                Label = "$Sum{f(k) @ k = a : b}",
                Category = "Numerical Methods",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "f(k)", Type = ParameterType.Expression, Description = "Expression to sum" },
                    new SnippetParameter { Name = "k", Type = ParameterType.Scalar, Description = "Counter variable" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Integer, Description = "Start value" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Integer, Description = "End value" }
                ]
            },
            new SnippetItem
            {
                Insert = "$Product{§ @ § = § : §}",
                Description = "Iterative product",
                Label = "$Product{f(k) @ k = a : b}",
                Category = "Numerical Methods",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "f(k)", Type = ParameterType.Expression, Description = "Expression to multiply" },
                    new SnippetParameter { Name = "k", Type = ParameterType.Scalar, Description = "Counter variable" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Integer, Description = "Start value" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Integer, Description = "End value" }
                ]
            },
            new SnippetItem
            {
                Insert = "$Repeat{§ @ § = § : §}",
                Description = "Iterative expression block with counter",
                Label = "$Repeat{expr @ k = a : b}",
                Category = "Numerical Methods",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "expr", Type = ParameterType.Expression, Description = "Expression block" },
                    new SnippetParameter { Name = "k", Type = ParameterType.Scalar, Description = "Counter variable" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Integer, Description = "Start value" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Integer, Description = "End value" }
                ]
            },
            new SnippetItem
            {
                Insert = "$While{§; §}",
                Description = "Iterative expression block with condition",
                Label = "$While{condition; expressions}",
                Category = "Numerical Methods",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "condition", Type = ParameterType.Boolean, Description = "Loop condition" },
                    new SnippetParameter { Name = "expressions", Type = ParameterType.Expression, Description = "Expression block" }
                ]
            },

            // ============================================
            // EXPRESSION BLOCKS
            // ============================================
            new SnippetItem
            {
                Insert = "$Block{§}",
                Description = "Multiline expression block",
                Category = "Numerical Methods",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "expressions", Type = ParameterType.Expression, Description = "Expressions separated by semicolons" }
                ]
            },
            new SnippetItem
            {
                Insert = "$Inline{§}",
                Description = "Inline expression block",
                Category = "Numerical Methods",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "expressions", Type = ParameterType.Expression, Description = "Expressions separated by semicolons" }
                ]
            },

            // ============================================
            // GRAPHING AND PLOTTING
            // ============================================
            new SnippetItem
            {
                Insert = "$Plot{§ @ § = § : §}",
                Description = "Simple function plot",
                Label = "$Plot{f(x) @ x = a : b}",
                Category = "Plotting",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "f(x)", Type = ParameterType.Expression, Description = "Function to plot" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Variable" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Scalar, Description = "Start of range" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Scalar, Description = "End of range" }
                ]
            },
            new SnippetItem
            {
                Insert = "$Plot{§ | § @ § = § : §}",
                Description = "Parametric plot",
                Label = "$Plot{x(t) | y(t) @ t = a : b}",
                Category = "Plotting",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "x(t)", Type = ParameterType.Expression, Description = "X coordinate function" },
                    new SnippetParameter { Name = "y(t)", Type = ParameterType.Expression, Description = "Y coordinate function" },
                    new SnippetParameter { Name = "t", Type = ParameterType.Scalar, Description = "Parameter" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Scalar, Description = "Start of range" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Scalar, Description = "End of range" }
                ]
            },
            new SnippetItem
            {
                Insert = "$Plot{§ & § @ § = § : §}",
                Description = "Multiple function plot",
                Label = "$Plot{f1 & f2 & ... @ x = a : b}",
                Category = "Plotting",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "f1(x)", Type = ParameterType.Expression, Description = "First function" },
                    new SnippetParameter { Name = "f2(x)", Type = ParameterType.Expression, Description = "Second function" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "Variable" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Scalar, Description = "Start of range" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Scalar, Description = "End of range" }
                ]
            },
            new SnippetItem
            {
                Insert = "$Map{§ @ § = § : § & § = § : §}",
                Description = "2D color map of a 3D surface",
                Label = "$Map{f(x;y) @ x = a:b & y = c:d}",
                Category = "Plotting",
                KeywordType = "Command",
                Parameters =
                [
                    new SnippetParameter { Name = "f(x;y)", Type = ParameterType.Expression, Description = "Surface function" },
                    new SnippetParameter { Name = "x", Type = ParameterType.Scalar, Description = "X variable" },
                    new SnippetParameter { Name = "a", Type = ParameterType.Scalar, Description = "X start" },
                    new SnippetParameter { Name = "b", Type = ParameterType.Scalar, Description = "X end" },
                    new SnippetParameter { Name = "y", Type = ParameterType.Scalar, Description = "Y variable" },
                    new SnippetParameter { Name = "c", Type = ParameterType.Scalar, Description = "Y start" },
                    new SnippetParameter { Name = "d", Type = ParameterType.Scalar, Description = "Y end" }
                ]
            }
        ];
    }
}