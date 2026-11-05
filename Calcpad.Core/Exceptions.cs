using System;
using System.Xml.Linq;
namespace Calcpad.Core
{
    internal static class Exceptions
    {
        internal enum Items
        {
            Index,
            Count,
            NumRows,
            NumCols,
            Argument,
            Result,
            Variable,
            IndexTarget,
            Limit,
            Value,
        }

        private static string ItemToString(Items item) =>
            item switch
            {
                Items.Index => Messages.Index,
                Items.Count => Messages.Count,
                Items.NumRows => Messages.Number_of_rows,
                Items.NumCols => Messages.Number_of_columns,
                Items.Argument => Messages.Argument,
                Items.Result => Messages.Result,
                Items.Variable => Messages.Variable,
                Items.IndexTarget => Messages.Index_target,
                Items.Limit => Messages.Limit,
                Items.Value => Messages.Value,
                _ => throw new ArgumentException(Messages.Invalid_item),
            };

        internal static MathParserException IncompleteExpression() =>
            new(Messages.Incomplete_expression, "CPD-3407");

        internal static MathParserException MissingLeftBracket() =>
            new(Messages.Missing_left_bracket, "CPD-3102");

        internal static MathParserException MissingRightBracket() =>
            new(Messages.Missing_right_bracket, "CPD-3101");

        internal static MathParserException InvalidSymbol(char c) =>
            new(string.Format(Messages.Invalid_symbol_0, c), "CPD-3401");

        internal static MathParserException InvalidUnits(string s) =>
            new(string.Format(Messages.Invalid_units_0, s), "CPD-3310");

        internal static MathParserException InvalidLiteral(string s, string literal) =>
            new(string.Format(Messages.Cannot_evaluate_0_as_1, s, literal), "CPD-5101");

        internal static MathParserException InvalidNumber(string s) =>
            InvalidLiteral(s, "number");

        internal static MathParserException InvalidOperator(char c) =>
            new(string.Format(Messages.Invalid_operator_0, c), "CPD-3401");

        internal static MathParserException PowerNotUnitless() =>
            new(Messages.Power_must_be_unitless, "CPD-5401");

        internal static MathParserException ResultIsNotUnits() =>
            new(Messages.The_expression_on_the_right_does_not_evaluate_to_units, "CPD-5402");

        internal static MathParserException CannotEvaluateFunction(string s) =>
            new(string.Format(Messages.Cannot_evaluate_the_function_f_for_v_equals_0, s), "CPD-5601");

        internal static MathParserException FunctionNotDefined(string s) =>
            new(string.Format(Messages.The_function_f_is_not_defined_for_v_equals_0, s), "CPD-5602");

        internal static MathParserException InconsistentUnits(string u1, string u2) =>
            new(string.Format(Messages.Inconsistent_units_0_and_1, u1, u2), "CPD-5403");

        internal static MathParserException IterationLimits(string l1, string l2) =>
            new(string.Format(Messages.Limits_out_of_range_0_1, l1, l2), "CPD-5603");

        internal static MathParserException InvalidUnitsFunction(string function, string unit) =>
            new(string.Format(Messages.Invalid_units_for_function_0_1, function, unit), "CPD-5404");

        internal static MathParserException RootUnitless() =>
            new(Messages.Root_index_must_be_unitless, "CPD-5405");

        internal static MathParserException RootComplex() =>
            new(Messages.Root_index_cannot_be_a_complex_number, "CPD-5406");

        internal static MathParserException RootInteger() =>
            new(Messages.Root_index_must_be_integer_more_than_1, "CPD-5407");

        internal static MathParserException FactorialArgumentOutOfRange() =>
            new(Messages.Argument_out_of_range_for_n_factorial, "CPD-5408");

        internal static MathParserException FactorialArgumentUnitless() =>
            new(Messages.The_argument_of_n_factorial_must_be_unitless, "CPD-5409");

        internal static MathParserException FactorialArgumentPositiveInteger() =>
            new(Messages.The_argument_of_n_factorial_must_be_a_positive_integer, "CPD-5410");

        internal static MathParserException FactorialArgumentComplex() =>
            new(Messages.The_argument_of_n_factorial_cannot_be_complex, "CPD-5411");

        internal static MathParserException RemainderUnits(string u1, string u2) =>
            new(string.Format(Messages.Cannot_evaluate_remainder_0_1_Denominator_must_be_unitless, u1, u2), "CPD-5412");

        internal static MathParserException BothValuesInteger() =>
            new(Messages.Both_values_must_be_integers, "CPD-5413");

        internal static MathParserException VariableNotExist(string name) =>
            new(string.Format(Messages.Variable_0_does_not_exist, name), "CPD-3410");

        internal static MathParserException UnitNotExist(string name) =>
            new(string.Format(Messages.Unit_0_does_not_exist, name), "CPD-3310");

        internal static MathParserException InteruptedByUser() =>
            new(Messages.Interrupted_by_user, "CPD-5901");

        internal static MathParserException CalculationsNotActive() =>
            new(Messages.Calculations_not_active, "CPD-5902");

        internal static MathParserException ExpressionEmpty() =>
            new(Messages.Expression_is_empty, "CPD-5102");

        internal static MathParserException MissingOperand() =>
            new(Messages.Missing_operand, "CPD-5103");

        internal static MathParserException InvalidFunction(string s) =>
            new(string.Format(Messages.Invalid_function_0, s), "CPD-3305");

        internal static MathParserException InvalidVariableName(string s) =>
            new(string.Format(Messages.Invalid_variable_name_0, s), "CPD-3201");

        internal static MathParserException CannotEvaluateAsType(string literal, string type) =>
            new(string.Format(Messages.Cannot_evaluate_0_as_1, literal, type), "CPD-5104");

        internal static MathParserException StackLeak() =>
            new(Messages.Stack_memory_leak_Invalid_expression, "CPD-5105");

        internal static MathParserException StackEmpty() =>
            new(Messages.Stack_empty_Invalid_expression, "CPD-5106");

        internal static MathParserException UndefinedInputField() =>
            new(Messages.Undefined_input_field, "CPD-5107");

        internal static MathParserException UndefinedVariableOrUnits(string s) =>
            new(string.Format(Messages.Undefined_variable_or_units_0, s), "CPD-3301");

        internal static MathParserException ErrorEvaluatingAsFunction(string s) =>
            new(string.Format(Messages.Error_evaluating_0_as_function, s), "CPD-5301");

        internal static MathParserException ErrorEvaluatingAsFunctionOrOperator(string s) =>
            new(string.Format(Messages.Error_evaluating_0_as_function_or_operator, s), "CPD-5302");

        internal static MathParserException CannotRewriteUnits(string s) =>
            new(string.Format(Messages.Cannot_rewrite_existing_units_0, s), "CPD-5303");

        internal static MathParserException InconsistentTargetUnits(string sourceUnits, string targetUnits) =>
            new(string.Format(Messages.The_calculated_units_0_are_inconsistent_with_the_target_units_1, sourceUnits, targetUnits), "CPD-5414");

        internal static MathParserException InvalidCharacter(char c) =>
            new(string.Format(Messages.Invalid_character_exception, c), "CPD-3201");

        internal static MathParserException ImproperAssignment() =>
            new(Messages.Improper_use_of_the_assignment_operator_equals, "CPD-3404");

        internal static MathParserException MissingLeftSolverBracket() =>
            new(Messages.Missing_left_bracket_in_solver_command, "CPD-3106");

        internal static MathParserException MissingRightSolverBracket() =>
            new(Messages.Missing_right_bracket_in_solver_command, "CPD-3105");

        internal static MathParserException InvalidMacro(string s) =>
            new(string.Format(Messages.Invalid_macro_identifier_0, s), "CPD-3303");

        internal static MathParserException InvalidSolver(string s) =>
            new(string.Format(Messages.Invalid_solver_command_definition_0, s), "CPD-3402");

        internal static MathParserException ErrorParsingUnits(string s) =>
            new(string.Format(Messages.Error_parsing_0_as_units, s), "CPD-3310");

        internal static MathParserException ErrorParsingNumber(string s) =>
            new(string.Format(Messages.Error_parsing_0_as_number, s), "CPD-5108");

        internal static MathParserException MissingDelimiter(char delimiter, string script) =>
            new(string.Format(Messages.Missing_delimiter_0_in_solver_command_1, delimiter, script), "CPD-3406");

        internal static MathParserException MultipleAssignments(string s) =>
            new(string.Format(Messages.More_than_one_operator_equals_in_0, s), "CPD-5109");

        internal static MathParserException NotConstantExpression(string s) =>
            new(string.Format(Messages.The_expression_on_the_right_side_must_be_constant_0, s), "CPD-5304");

        internal static MathParserException InconsistentUnits1(string variable, string units) =>
            new(string.Format(Messages.Inconsistent_units_for_0_equals_1, variable, units), "CPD-5415");

        internal static MathParserException InconsistentUnits2(string variable, string u1, string u2) =>
            new(string.Format(Messages.Inconsistent_units_for_0_equals_1_2, variable, u1, u2), "CPD-5416");

        internal static MathParserException NoSolution(string s) =>
            new(string.Format(Messages.No_solution_for_0, s), "CPD-5604");

        internal static MathParserException RecursionNotAllowed(string s) =>
            new(string.Format(Messages.Recursion_is_not_allowed_in_function_definition_0, s), "CPD-5305");

        internal static MathParserException AssignmentPreceded() =>
            new(Messages.The_assignment_equals_must_be_preceded_by_custom_function_or_variable, "CPD-3203");

        internal static MathParserException AssignmentNotFirst() =>
            new(Messages.Assignment_equals_must_be_the_first_operator_in_the_expression, "CPD-3404");

        internal static MathParserException InvalidSyntax(string s) =>
           new(string.Format(Messages.Invalid_syntax_0, s), "CPD-3401");

        internal static MathParserException InvalidSyntax(string s1, string s2) =>
            new(string.Format(Messages.Invalid_syntax_0_1, s1, s2), "CPD-3401");

        internal static MathParserException UnexpectedDelimiter() =>
            new(Messages.Unexpected_delimiter, "CPD-5110");

        internal static MathParserException InvalidNumberOfArguments() =>
            new(Messages.Invalid_number_of_arguments, "CPD-3302");

        internal static MathParserException ResultNotReal(string s) =>
            new(string.Format(Messages.The_result_is_not_a_real_number_0, s), "CPD-5417");

        internal static MathParserException MissingFunctionParameter() =>
            new(Messages.Missing_parameter_in_function_definition, "CPD-3206");

        internal static MathParserException MissingFunctionDelimiter() =>
            new(Messages.Missing_delimiter_in_function_definition, "CPD-5111");

        internal static MathParserException InvalidFunctionToken(string name) =>
            new(string.Format(Messages.Invalid_token_in_function_definition_0, name), "CPD-3202");

        internal static MathParserException CircularReference(string name) =>
            new(string.Format(Messages.Circular_reference_detected_for_function_0, name), "CPD-5306");

        internal static MathParserException CircularMacroReference(string name) =>
            new(string.Format(Messages.Circular_reference_detected_for_macro_0, name), "CPD-3317");

        internal static MathParserException InvalidFunctionDefinition() =>
            new(Messages.Invalid_function_definition_exception, "CPD-3202");

        internal static MathParserException ArgumentOutOfRange(string function) =>
            new(string.Format(Messages.Argument_out_of_range_for_0_x, function), "CPD-5418");

        internal static MathParserException ConditionEmpty() =>
            new(Messages.Condition_cannot_be_empty, "CPD-5201");

        internal static MathParserException ConditionNotInitialized() =>
            new(Messages.Condition_block_not_initialized_with_if, "CPD-3105");

        internal static MathParserException DuplicateElse() =>
            new(Messages.Duplicate_else_in_condition_block, "CPD-5202");

        internal static MathParserException ElseIfAfterElse() =>
            new(Messages.else_if_is_not_allowed_after_else_in_condition_block, "CPD-5203");

        internal static MathParserException ConditionComplex() =>
            new(Messages.Condition_cannot_evaluate_to_a_complex_number, "CPD-5419");

        internal static MathParserException ConditionResultInvalid(string s) =>
            new(string.Format(Messages.Condition_result_is_invalid_0, s), "CPD-5420");

        internal static MathParserException UndefinedMacro(string s) =>
            new(string.Format(Messages.Macro_not_defined_0, s), "CPD-3303");

        internal static MathParserException RequiredParameterAfterOptional(string name) =>
            new(string.Format(Messages.Required_parameter_after_optional_0, name), "CPD-3207");

        internal static MathParserException UnknownKeywordArgument(string name) =>
            new(string.Format(Messages.Unknown_keyword_argument_0, name), "CPD-3315");

        internal static MathParserException DuplicateArgument(string name) =>
            new(string.Format(Messages.Duplicate_argument_0, name), "CPD-5112");

        internal static MathParserException MissingMapItem(string s) =>
            new(string.Format(Messages.Missing_0_in_surface_map_command, s), "CPD-5113");

        internal static MathParserException MissingMapDelimiter(char delimiter, string script) =>
            new(string.Format(Messages.Missing_delimiter_0_in_surface_map_command_1, delimiter, script), "CPD-3406");

        internal static MathParserException MissingPlotDelimiter(char delimiter, string script) =>
            new(string.Format(Messages.Missing_delimiter_0_in_plot_command, delimiter, script), "CPD-3406");

        internal static MathParserException MissingPlotItem(string item, string script) =>
            new(string.Format(Messages.Missing_0_in_plot_command_1, item, script), "CPD-5113");

        internal static MathParserException MultiplePlotDelimiters(string script) =>
            new(string.Format(Messages.More_than_one_delimiter_in_plot_command, script), "CPD-5120");

        internal static MathParserException PlotLimitsIdentical() =>
            new(Messages.The_limits_of_plot_area_are_identical, "CPD-5421");

        internal static MathParserException ErrorWritingPngFile(string path) =>
            new(string.Format(Messages.Error_writing_a_png_file_to_0, path), "CPD-5701");

        internal static MathParserException ErrorWritingSvgFile(string path) =>
            new(string.Format(Messages.Error_writing_a_svg_file_to_0, path), "CPD-5702");

        internal static MathParserException ErrorConvertingPngToBase64() =>
            new(Messages.Error_converting_png_to_Base64, "CPD-5703");

        internal static MathParserException InconsistentUnitsOperation(string ua, char op, string ub) =>
            new(string.Format(Messages.Inconsistent_units_0_1_2, ua, op, ub), "CPD-5422");

        internal static MathParserException UnitsToComplexPower() =>
            new(Messages.Units_cannot_be_raised_to_complex_power, "CPD-5423");

        internal static MathParserException CannotEvaluateRemainder(string ua, string ub) =>
            new(string.Format(Messages.Cannot_evaluate_remainder_0_1_The_denominator_must_be_unitless, ua, ub), "CPD-5424");

        internal static MathParserException MissingVectorOpeningBracket() =>
            new(Messages.Missing_vector_opening_bracket, "CPD-3104");

        internal static MathParserException BracketMismatch() =>
            new(Messages.Bracket_mismatch, "CPD-5114");

        internal static MathParserException IndexOutOfRange(string index) =>
            new(string.Format(Messages.Index_out_of_range_0, index), "CPD-5501");

        internal static MathParserException CannotAssignVectorToScalar() =>
            new(Messages.Cannot_assign_vector_to_scalar, "CPD-5502");

        internal static MathParserException MissingVectorClosingBracket() =>
            new(Messages.Missing_vector_closing_bracket, "CPD-3103");

        internal static MathParserException MustBeScalar(Items item) =>
            new(ItemToString(item) + Messages._must_be_scalar, "CPD-5503");

        internal static MathParserException MustBeReal(Items item) =>
            new(ItemToString(item) + Messages._must_be_real, "CPD-5504");

        internal static MathParserException MustBePositiveInteger(Items item) =>
            new(ItemToString(item) + Messages._must_be_positive_integer, "CPD-5505");

        internal static MathParserException MustBeMatrix(Items item) =>
            new(ItemToString(item) + Messages._must_be_matrix, "CPD-5506");

        internal static MathParserException MustBeVector(Items item) =>
            new(ItemToString(item) + Messages._must_be_vector, "CPD-5507");

        internal static MathParserException StepCannotBeZero() =>
            new(Messages.Step_cannot_be_zero, "CPD-5508");

        internal static MathParserException VectorSizeLimit() =>
            new(string.Format(Messages.Vector_size_cannot_exceed_0, Vector.MaxLength), "CPD-5509");

        internal static MathParserException CrossProductVectorDimensions() =>
            new(Messages.Cross_product_is_definedonly_for_vectors_with_2_and_3_elements, "CPD-5510");

        internal static MathParserException ComplexNumberInRealMode() =>
            new ComplexInRealModeException(Messages.Complex_numbers_are_not_supported_outside_of_complex_mode);

        internal static MathParserException ComplexVariableInRealMode(string name) =>
            new ComplexInRealModeException(string.Format(Messages.Complex_variable_0_is_not_supported_outside_of_complex_mode, name));

        internal static MathParserException ComplexFunctionInRealMode(string name) =>
            new ComplexInRealModeException(string.Format(Messages.Function_0_with_complex_values_is_not_supported_outside_of_complex_mode, name));

        internal static MathParserException FunctionOnlyRealMode(string name) =>
            new(string.Format(Messages.Function_0_is_not_defined_in_complex_mode, name), "CPD-5511");

        internal static MathParserException MatrixDimensions() =>
            new(Messages.Matrix_dimensions_do_not_match, "CPD-5512");

        internal static MathParserException MatrixNotSquare() =>
            new(Messages.Matrix_must_be_square, "CPD-5513");

        internal static MathParserException InvalidLpNormArgument() =>
            new(Messages.The_Lp_norm_argument_must_be_p_ge_1, "CPD-5514");

        internal static MathParserException MatrixMustBeSymmetric() =>
            new(Messages.Matrix_must_be_symmetric, "CPD-5515");

        internal static MathParserException MatrixNotPositiveDefinite() =>
            new(Messages.Matrix_is_not_positive_definite, "CPD-5516");

        internal static MathParserException MatrixSingular() =>
            new(Messages.Matrix_is_singular, "CPD-5517");

        internal static MathParserException MatrixCloseToSingular() =>
            new(Messages.Matrix_is_singular_or_close_to_singular, "CPD-5518");

        internal static MathParserException ComplexVectorsAndMatricesNotSupported() =>
            new(Messages.Vectors_and_matrices_are_not_supported_in_complex_mode, "CPD-5519");

        internal static MathParserException CannotInterpolateWithNonScalarValue() =>
            new(Messages.Cannot_interpolate_with_non_scalar_value, "CPD-5520");

        internal static MathParserException JacobiFailed() =>
            new(Messages.Jacobi_iteration_failed, "CPD-5521");

        internal static MathParserException InvalidOperand(string value) =>
            new(string.Format(Messages.Invalid_operand_0, value), "CPD-5522");

        internal static MathParserException InvalidArgument(string value) =>
            new(string.Format(Messages.Invalid_argument_0, value), "CPD-5523");

        internal static MathParserException ArgumentNotEigenvalue(string value) =>
            new(string.Format(Messages.The_argument_is_not_an_eigenvalue_0, value), "CPD-5524");

        internal static MathParserException QLAlgorithmFailed() =>
            new(Messages.The_QL_algorithm_failed_to_converge, "CPD-5525");

        internal static MathParserException MatirixSizeLimit() =>
            new(string.Format(Messages.Matrix_size_cannot_exceed_0, Matrix.MaxSize), "CPD-5526");

        internal static MathParserException EmbeddedDataSizeLimit() =>
            new(string.Format(Messages.Embedded_data_cannot_exceed_0_MB, ExpressionParser.MaxEmbeddedDataSize / (1024 * 1024)), "CPD-5527");

        internal static MathParserException DatagridSizeLimit() =>
            new(string.Format(Messages.Datagrid_size_cannot_exceed_0_cells, UiDto.MaxSize), "CPD-5528");

        internal static MathParserException MatrixNotHigh() =>
            new(Messages.The_number_of_matrix_rows_must_be_greater_than_or_equal_to_the_number_of_columns, "CPD-5529");

        internal static MathParserException FunctionDefinitionInSolver() =>
            new(Messages.Function_definition_not_allowed_in_solver_block, "CPD-5115");

        internal static MathParserException InvalidFormatString(string s) =>
            new(string.Format(Messages.Invalid_format_string_0, s), "CPD-3601");

        internal static MathParserException VariableNotMatrix(string name) =>
            new(string.Format(Messages.The_variable_is_not_a_matrix_0, name), "CPD-5530");

        internal static MathParserException FileNotFound(string path) =>
            new(string.Format(Messages.File_not_found_0, path), "CPD-5704");

        internal static MathParserException MissingFileName() =>
            new(Messages.Missing_file_name, "CPD-5116");

        internal static MathParserException InvalidType(char type) =>
            new(string.Format(Messages.Invalid_type_0, type), "CPD-5117");

        internal static MathParserException PathNotFound(string path) =>
            new(string.Format(Messages.Path_not_found_0, path), "CPD-5705");

        internal static MathParserException FileFormatNotSupported(string ext) =>
            new(string.Format(Messages.ThisFileFormatIsNotSupported0, ext), "CPD-5706");

        internal static MathParserException MustBeHpMatrix(Items item) =>
            new(ItemToString(item) + Messages.MustBeHpMatrix, "CPD-5531");

        internal static MathParserException MustBeHpVector(Items item) =>
             new(ItemToString(item) + Messages.MustBeHpVector, "CPD-5532");

        internal static MathParserException MatrixOneOrTwoRows() =>
            new(Messages.TheInputMatrixForFFTMustHaveOneRowForRealAndTwoRowsForComplexData, "CPD-5533");
        internal static MathParserException MissingMatrixIndex(string matrix) =>
            new(string.Format(Messages.MissingMatrixIndexIn0, matrix), "CPD-5118");

        internal static MathParserException CounterMustBeASingleVariableName() =>
            new(Messages.CounterMustBeASingleVariableName, "CPD-3406");

        internal static MathParserException InvalidNumberOfIndexes() =>
            new(Messages.InvalidNumberOfIndexes, "CPD-5119");

        internal static MathParserException InfiniteLoop(int maxCount) =>
            new(string.Format(Messages.PossiblyInfiniteLoopDetected, maxCount), "CPD-5903");

        internal static MathParserException CannotModifyConstant(string name) =>
            new(string.Format(Messages.CannotModifyConstant, name), "CPD-3409");
    }
}