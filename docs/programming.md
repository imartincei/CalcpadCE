# Programming

## Input Forms

If you have a long and complicated problem or you want to share your solution with others, it is a good idea to create an input form.
It is very easy to do that with CalcpadCE.
Just put the `#UI` keyword in front of the definitions that need to be entered:

```calcpad
#UI 'Span -'L = 6m
#UI 'Load -'q = 25kN/m
M = q*L^2/8
```

The default values stay in the document, so it still calculates as an ordinary worksheet.
When you switch the results pane to **Input** mode, every `#UI` line is rendered as a control and the rest of the document is shown below it.
Change a value, and the whole document recalculates.
**Report** mode shows the printable report with the values you entered.
A document that declares `#UI` inputs opens as its input form, so the user can fill it in straight away.

This approach has the following advantages:

- The user can see clearly which parameters should be entered.
    You can also provide pictures and additional explanations.
    This is more comprehensible for the user, especially if the program is developed by someone else;
- The person filling in the form never edits the source code.
    This prevents an inexperienced user from accidentally damaging the calculation formulas.

Entered values are saved at the end of the file, separately from the defaults in the code.
See [Saving what was entered](new-ui-mode.md#saving-what-was-entered).

If you save the document as a compiled "**\*.cpdz**" worksheet, the source code becomes completely inaccessible.
The worksheet opens as its input form and cannot be unlocked.
That is how you can protect your source code from unauthorized copying, viewing and modification.
See [Save As Compiled Worksheet](new-exports.md#save-as-compiled-worksheet).

!!! note
    The `?` input fields of earlier versions, such as `a = ?{2}`, are deprecated and no longer create input fields.
    See the [Migration Guide](migration-guide.md#input-syntax-is-deprecated) for how to convert them to `#UI`.

## Advanced UI Controls

Besides simple input boxes, you can use drop-downs, radio buttons, checkboxes and editable grids in your worksheets.
Set the control type and its options in a JSON block after `#UI`:

```calcpad
#UI {"type": "dropdown", "keys": ["Low", "Medium", "High"], "values": ["1", "2", "3"]} grade = 1
#UI {"type": "radio", "keys": ["Steel", "Concrete"], "values": ["200GPa", "25GPa"]} E = 200GPa
#UI {"type": "checkbox"} useSteel = 1
#UI M = [1; 2; 3 | 4; 5; 6]
```

`keys` are the labels shown to the user, and `values` are substituted into the calculation, one per key.
A vector or matrix becomes an editable grid.
You don't have to write the JSON by hand: the [Properties tab](new-calcpad-panel.md#properties) has a form for it.

See [UI Mode](new-ui-mode.md) for every control type and property, and for styling the controls with CSS.

## Output Control

You can easily specify which parts of the code should be visible or hidden in the output.
Unlike conditional execution, the hidden code is always calculated.
It is just not displayed.
The following keywords can be used for that purpose:

- `#Hide` hides the contents after the current line;
- `#Pre` shows the contents in the input form and hides it in the report (see [Input Forms](#input-forms) above);
- `#Post` shows the contents in the report and hides it in the input form;
- `#Show` always shows the contents (revoke all other keywords);

Each of the above keywords affects the content after the current line and overrides the previous one.
You can use them to hide long and repetitive calculations that should not be visible.
You can use the `#Pre` command to add some directions about filling the input data and `#Post` to hide the calculation algorithm during data input.
The **Preview** mode shows both `#Pre` and `#Post` content.
See [`#pre` and `#post`](new-ui-mode.md#pre-and-post).

You can also modify the display of the equations as follows:

- `#Val` shows only the final result as a single value;
- `#Equ` shows both the equation and the calculated result (default);
- `#Noc` shows only the equation, without results (no calculations).

Each of the above keywords overrides the other.
You can use `#Val` to create a table with values, but without the formulas, like in Excel.

Each of these keywords can be closed with its `#end` form, such as `#end hide` or `#end val`, which restores the state that was in effect before it.
They also accept an optional condition, e.g. `#hide x == 5`, which applies them only when the condition is true.
See [Visibility Directives](new-visibility-directives.md).

## HTML and Markdown Blocks

By default, every line is parsed as math or a comment.
`#html` and `#markdown` switch the lines that follow to raw HTML or Markdown, until the matching `#end html` or `#end markdown`:

```calcpad
#markdown
| Case | Load |
|------|------|
| Dead | 5 kN |
| Live | 3 kN |
#end markdown
```

See [Parsing Modes](new-parsing-modes.md).

## Conditional Execution

Sometimes the solution has to continue in different ways, depending on some intermediate values.
Such feature is included in CalcpadCE, similarly to other programming languages.
It is called "conditional execution block" and has the following general form:

```calcpad
#If condition1
    contents if condition1 is satisfied
#Else If condition2
    contents if condition2 is satisfied
#Else If condition3
    contents if condition3 is satisfied
. . .
#Else
    contents if none of the conditions is satisfied
#end if
```

Shorter forms are also possible:

```calcpad
#If condition
    contents if the condition is satisfied
#Else
    contents if the condition is not satisfied
#end if
```

or:

```calcpad
#If condition
    contents if the condition is satisfied
#end if
```

Condition blocks affect not only the calculation path but also the report content like text and images.
The "#" symbol must be the first one in the line.
At the place of "**condition**" you can put any valid expression.
Normally, a comparison is used like "#If *a* < 0", but it is not obligatory.
If it evaluates to any non-zero number, the condition is assumed to be satisfied.
Otherwise, it is not satisfied.
Any result which absolute value is ≤ 0.00000001 is assumed to be zero.

Let us look again at the quadratic equation example that we used earlier.
If we enter "*c* = 5", the discriminant will be negative, and the result will be NaN.
This is not a very intelligent way to finish a program.
What we need to do is to check if "*D* < 0" and if so, to provide a comprehensible message.
Otherwise, we have to calculate the roots.
We can do this, using conditional execution, as follows:

<img src="./media/image37.png" style="width:6.3986in;height:2.48934in" alt="Sample2" />

## Iteration Blocks

You can have simple iterations inside a CalcpadCE program.
For that purpose, you have to define a "**repeat-loop**" block:

```calcpad
#Repeat n
    code to be executed repeatedly
#Loop
```

The symbol *n* stands for the number of repetitions.
Instead of *n*, you can put a number, variable or any valid expression.
If the result of the expression is not integer, it is rounded to the nearest one.
You can exit the repeat-loop cycle prematurely by putting `#Break` inside the block.
It will make sense only if you combine it a conditional block.
Otherwise, it will always break at the same line, without performing any loops.
A typical "**repeat-break-loop**" will look like this:

```calcpad
#Repeat
    code to be executed repeatedly
    #If condition
        #Break
    #End if
    you can have more code here
#Loop
```

You can also use `#Continue` instead of `#Break` inside the condition.
The program will skip the remaining lines, return to the top of the loop block and continue with the next iteration.
You can omit the number of repetitions *n* only if you are sure that the condition will be satisfied, and you will leave the loop sooner or later.
However, to avoid infinite loops, the number of iterations is limited internally to 10 000 000.

Besides repetitive calculations, you can use loops to generate repetitive report content (like table rows).
If you want to hide the iteration details, you can use output control directives (see the previous section).
For example, you can enclose the "repeat-loop" block with `#Hide` and `#Show` statements.

Since version VM 7.0, two new iteration blocks were added: "for-loop" and "while-loop", as follows:

```calcpad
#For counter = start : end
    code to be executed repeatedly
#Loop
#While condition
    code to be executed repeatedly
#Loop
```

## Interactive (Step-by-Step) Execution

You can split an input form into steps with the `#Pause` keyword.
The form calculates down to the first `#Pause` and stops there.
The user can review the intermediate results, change the inputs above, and press **Next** to continue to the following step.
**Previous** steps back.

```calcpad
#UI x = 2
#UI n = 1000000
#pause
s = 0
#for i = 1 : n
    s = s + x/i^2
#loop
```

This is especially useful before slow parts of the calculation, so editing the earlier inputs stays responsive.
Outside the input form, `#Pause` is ignored and the whole document is calculated.
See [`#pause` in UI mode](new-ui-mode.md#pause-in-ui-mode).

!!! note
    The `#Input` keyword is deprecated and has no effect.
    Declare the inputs with `#UI` and use `#Pause` instead.

## Modules (Include)

CalcpadCE allows you to include content from external files in your worksheet.
If you have pieces of code that is repeated in different worksheets, you can organize it in modules and reuse it multiple times.
Also, if you have a longer worksheet, you can split it into modules that will be easier to maintain.
Then, you can include them into the main file by using the following statement:

```calcpad
#include filename
```

The "*filename*" can be a full path to a local file, or a path relative to the folder of the current file.
If the file is the same folder as the current one, you can specify only the filename.
A path can also start with `{project}`, `{library}` or `{user}`, and environment variables such as `%APPDATA%` are expanded.
See [Path root tokens](new-includes.md#path-root-tokens-project-and-library).

By default, CalcpadCE will include the whole contents of the external module.
However, you can prevent some parts from inclusion by making them local.
To start a "local" section in a module, add a new line, containing the `#local` keyword.
To end a "local" section (or start a "global" one), add a new line with the `#global` keyword.
CalcpadCE supports multiple levels of inclusions.
That means that the included file, in its turn, can reference other files and so on, up to 20 levels deep.
See [Includes and File Reads](new-includes.md).

## Macros and String Variables

Macros and string variables are convenient ways to organize your code inside a single file and prevent repetitions.
They can be inline or multiline.
Unlike string variables, macros can have parameters.
You can define them by using the following statements:

Inline string variable:

```calcpad
#def variable_name$ = content
```

Multiline string variable:

```calcpad
#def variable_name$
    content line 1
    content line 2
    ...
#end def
```

Inline string macro:

```calcpad
#def macro_name$(param1$; param2$;...) = content
```

Multiline string macro:  

```calcpad
#def macro_name$(param1$; param2$;...)
    content line 1
    content line 2  
    ...
#end def
```

Names of string variables, macros, and their parameters can contain small and capital Latin letters, digits and underscore "\_".
They cannot start with a digit and must end with the "\$" symbol.
The contents can be virtually any string.
It is not necessary to be a valid CalcpadCE expression, since it is not processed by the parser at this stage.
However, other macro/string variable definitions are not allowed inside.
You can insert only references to previously defined ones.
You can use `#include` inside macros, but only if the included file does not contain other macros.

After a string variable is defined, you can use it anywhere in the code by writing its name (with the ending "\$").
The same is for macros, but you also need to specify values for parameters.
Macro parameters can have default values, and macros can be called with keyword arguments such as `calc$(1; c$ = 7)`.
See [Keyword Arguments](new-keyword-arguments.md).
When a name ending in "\$" is treated as a macro call is controlled by the **Macro Calls** setting.
See [Settings → In a document](new-settings.md#in-a-document).

Macros and string variables are preprocessed and rewritten before the actual parsing is performed.
As a result, intermediate (unwrapped) code is generated.
You can review it by switching the results pane to **Unwrapped** mode.

The [linter](new-linter.md) checks a macro body with the arguments of each call.
An error in the body is marked on the call, and on the line in the body that caused it, each linked to the other.
See [Errors inside macros and functions](new-linter.md#errors-inside-macros-and-functions).
If a macro call or an `#include` fails during preprocessing, that line is shown as an error in the output, and the rest of the code is still calculated.
Errors are also listed in the **Problems** panel, where you can click one to go to the respective line in your code.

## Import/Export of External Data

You can import and export numerical data from/to text, CSV and Excel files.
Inside CalcpadCE, data should be stored in a matrix/vector variable.
You can also read and write partial data by specifying the desired range.
Structured storage of special matrices is supported for saving space.
File paths can start with `{project}`, `{library}` or `{user}`, the same as for `#include`.
When `#write` and `#append` are allowed to write their files is set by the **Write files** option on the **Export** tab.
See [Exports → Write / append files](new-exports.md#write-append-files).
The following commands are available:

### Text/CSV Files

Reads data from the specified text/CSV file into the matrix/vector *M*.
The file must exist:

```calcpad
#read M from filename.txt@R1C1:R2C2 TYPE=R SEP=','
```

A read can also carry its data instead of naming a file, by giving a base64 [data URI](https://developer.mozilla.org/en-US/docs/Web/URI/Reference/Schemes/data) as the source.
Everything else is written as it always is — the range, the type and the separator apply to the carried data the same way:

```calcpad
#read M from data:text/csv;base64,MSwzCjIsNAo= TYPE=R SEP=','
```

The media type must be one of `text/csv`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` (**\*.xlsx**) or `application/vnd.ms-excel.sheet.macroEnabled.12` (**\*.xlsm**).
This is what [saving a compiled worksheet](new-exports.md#save-as-compiled-worksheet) writes, so that it runs with no data files beside it.

Writes data from matrix/vector *M* to the specified text/CSV file.
If the file exists, it is entirely overwritten.
Otherwise, a new file is created.
In all cases, the path to the file must exist.

```calcpad
#write M to filename.txt@R1C1:R2C2 TYPE=N SEP=','
```

Appends data from matrix/vector *M* to the specified text/CSV file.
If the file exists, the data is appended at the end of the existing file.
Otherwise, a new file is created.
In all cases, the path to the file must exist.

```calcpad
#append M to filename.txt@R1C1:R2C2 TYPE=N SEP=','
```

Command options:

- `M` the name of the matrix/vector that contains the data \[required\];
- `filename.txt` the name and path of the input/output file \[required\]. If the path is omitted, the file is assumed to be in the same folder.
    Extension is required.
    Any valid extension is allowed (except those for Excel), including **txt** and **csv**, as long as the data in the file is in text format;
- `@R1C1:R2C2` data range in the input file \[optional\]:
  - `R1C1` starting row and column indexes \[optional\]:
  - `R1` row index: includes capital letter “R”, followed by the number of the row \[optional\];
  - `C1`column index: capital letter “C”, followed by the number of the column \[optional\];
  - `:R2C2` ending row (R2) and column (C2) indexes as above \[optional\];

    Indexing starts at **1**. You can skip any of the starting and ending row/column indexes.
    In this case, the default values of 1 and matrix dimensions are taken.
    Starting indexes can be greater than ending ones.
    Some examples are given below:

    ![CSV data showing example ranges](media/Image%2048.png)

- `TYPE=R` The type of matrix/vector for structured storage \[optional\].  
    For the `#read` command, TYPE can be any of the following capital letters:
  - `R` rectangular matrix (default);
  - `C` column matrix;
  - `D` diagonal matrix;
  - `S` symmetric skyline matrix;
  - `L` lower triangular matrix;
  - `U` upper triangular matrix;
  - `V` vector.

    If you want to use the high-performance version of the type, add **\_hp** after the type letter.
    For example: **R_hp** or **S_hp** .  
    For column and diagonal matrices values can be stored either on a single line or in a column of one value per line.
    For diagonal matrices only the values along the main diagonal are stored, for lower triangular - only below the main diagonal, and for symmetric and upper triangular - only above the main diagonal.
    Vector values can be spread along multiple lines, but all are collected in a single vector consequently line-by-line.
    Examples for structured storage are provided below:

    ![Matrix examples](media/Image%2049.png)

    For the `#write` and `#append` commands TYPE can be one of the capital letters below:

  - `Y` Yes, the matrix structure is used;
  - `N` No, the matrix structure is not used (default);

    If “**N**” is selected, all matrices are stored as rectangular, regardless their type and internal structure.
    All elements after the last nonzero value on the row are skipped.
- `SEP=','` separator \[optional\]. You must specify a single character in quotes.
    For the \#read command it must correspond to the actual separator used in the input file.

The minimum allowed syntax for the above commands if all optional keywords are skipped is:

- `#read M from filename.txt` or
- `#write M to filename.txt` or
- `#append M to filename.txt`

### Excel Files

Reads the data from the specified Excel file into the matrix/vector *M*.
The file must exist as well as the specified worksheet:

```calcpad
#read M from filename.xlsx@Sheet1!A1:B2 TYPE=R
```

Writes data from matrix/vector *M* to the specified Excel file.
A new file with a single worksheet is created.
If the file exists, it is entirely overwritten.
The path to the file must exist;  

```calcpad
#write M to filename.xlsx@Sheet1!A1:B2 TYPE=N
```

Appends data from matrix/vector *M* to the specified Excel file.
If the file exists, the data is written in the existing file at the specified location.
Otherwise, a new file is created.
In all cases, the path to the file must exist.

```calcpad
#append M to filename.xlsx@Sheet1!A1:B2 TYPE=N
```

Command options:

- `M` the name of the matrix/vector that contains the data \[required\];
- `filename.xlsx` the name and path of the input/output file \[required\]. If the path is missing, the file is assumed to be in the same folder.
    The supported extensions are **xlsx** and **xlsm**;
- `@Sheet1` the name of the target worksheet \[optional\].
    If omitted the first worksheet is used for existing files and Sheet1 is assumed for newly created worksheets.
- `!A1:B2` target cell range \[optional\]:
  - `A1` starting cell reference \[optional\], where A is the column name and 1 is the row index;
  - `:B2` ending cell reference as above \[optional\];  
    Column names start at **A**, and row numbers start at **1**. You can skip any of the starting and ending column/row references.
    In this case, data is read to the first and last nonempty cells, respectively.
    The starting cell references can be greater than the ending ones.
    Examples for data import settings are provided below:

    ![Excel addressing examples](media/Image%2050.png)

    The behavior of data export commands `#write` and `#append` is a bit different.
    The starting reference indicates the location of the first element $M_{1,1}$ of the output matrix.
    So, even if it is greater than A1, it will not truncate the first rows and columns.
    Unless bound by the ending reference, the entire matrix will be written after the specified location.
    Otherwise, the remaining rows and columns after the ending reference will be truncated.
    For example: `#write M to filename.xlsx@Sheet1!C2` will produce the following output:

    ![Excel example](media/Image%2051.png){style="width: 300px;"}

- `TYPE=R` type of matrix/vector for structured storage \[optional\]. The same rules apply as for text/CSV files above.
